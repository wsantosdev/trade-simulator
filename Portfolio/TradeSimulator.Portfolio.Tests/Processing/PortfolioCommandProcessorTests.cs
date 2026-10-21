using Microsoft.Extensions.Logging.Abstractions;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradeSimulator.Portfolio.Tests.Processing;

public sealed class PortfolioCommandProcessorTests
{
    private readonly FakeStore _store = new();
    private readonly FakeIdempotency _idempotency = new();
    private readonly FakeResponses _responses = new();
    private readonly FakeNotifier _notifier = new();
    private readonly PortfolioCommandProcessor _processor;
    private readonly Guid _account = Guid.CreateVersion7();

    public PortfolioCommandProcessorTests() =>
        _processor = new(_store, _idempotency, _responses, _notifier, new Infrastructure.FakeMetrics(), TimeProvider.System, NullLogger<PortfolioCommandProcessor>.Instance);

    private Task Run(AssetOperation operation, string ticker, long quantity, Guid? id = null) =>
        _processor.ExecuteAsync(id ?? Guid.CreateVersion7(), new AssetCommand(operation, _account, ticker, quantity), default);

    private Task Sell(long quantity, Guid orderId, string ticker = "PETR4", Guid? id = null) =>
        _processor.ReserveAsync(id ?? Guid.CreateVersion7(), new SellOrder(orderId, _account, ticker, quantity), default);

    private Task Settle(Guid orderId, long executed, bool final, Guid? id = null) =>
        _processor.SettleOrderAsync(id ?? Guid.CreateVersion7(), orderId, executed, final, default);

    [Fact]
    public async Task First_deposit_creates_the_asset_and_the_next_one_increments_it()
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        await Run(AssetOperation.Deposit, "PETR4", 50);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(150, asset.Available.Value);
        Assert.Equal(2, _store.Responses.Count);
        Assert.Equal(2, _notifier.Notified.Count);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Success_response_reports_requested_quantity_and_balances()
    {
        var id = Guid.CreateVersion7();
        await Run(AssetOperation.Deposit, "petr4", 100, id);
        await Run(AssetOperation.Block, "PETR4", 30);

        var result = _store.Responses[1];
        Assert.Equal(30, result.RequestedQuantity);
        Assert.Equal(70, result.Available);
        Assert.Equal(30, result.Blocked);
        Assert.Equal(100, result.Total);
        Assert.Equal(id, _store.Responses[0].EnvelopeId);
    }

    [Fact]
    public async Task Withdraw_of_everything_leaves_a_zero_total_and_frees_the_slot()
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        await Run(AssetOperation.Block, "PETR4", 100);
        await Run(AssetOperation.Withdraw, "PETR4", 100);

        Assert.Equal(0, _store.Responses[^1].Total);
        await Run(AssetOperation.Deposit, "PETR4", 10);
        Assert.Equal(2, _store.Assets.Count);
    }

    [Fact]
    public async Task Unblock_above_blocked_is_a_typed_business_failure_and_keeps_the_key()
    {
        var id = Guid.CreateVersion7();
        await Run(AssetOperation.Deposit, "PETR4", 100);
        await Run(AssetOperation.Unblock, "PETR4", 10, id);

        var failure = Assert.Single(_responses.Failures);
        Assert.Equal(nameof(InsufficientBlockedQuantity), failure.ErrorType);
        Assert.Equal(10, failure.RequestedQuantity);
        Assert.Equal(100, failure.Available);
        Assert.Contains(id, _idempotency.Processed);
        Assert.Empty(_idempotency.Locked);
    }

    [Fact]
    public async Task Command_on_unknown_asset_fails_with_asset_not_found()
    {
        await Run(AssetOperation.Block, "PETR4", 10);

        Assert.Equal(nameof(AssetNotFound), Assert.Single(_responses.Failures).ErrorType);
    }

    [Theory]
    [InlineData("XX", 10, nameof(InvalidTicker))]
    [InlineData("PETR4", 0, nameof(InvalidQuantity))]
    [InlineData("PETR4", 1_000_001, nameof(QuantityAboveLimit))]
    public async Task Invalid_values_fail_with_typed_errors(string ticker, long quantity, string expected)
    {
        await Run(AssetOperation.Deposit, ticker, quantity);

        Assert.Equal(expected, Assert.Single(_responses.Failures).ErrorType);
        Assert.Empty(_store.Assets);
    }

    [Fact]
    public async Task Duplicated_envelope_is_discarded()
    {
        var id = Guid.CreateVersion7();
        await Run(AssetOperation.Deposit, "PETR4", 100, id);
        await Run(AssetOperation.Deposit, "PETR4", 100, id);

        Assert.Equal(100, Assert.Single(_store.Assets.Values).Available.Value);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Infrastructure_failure_never_writes_the_key_releases_the_lock_and_rethrows()
    {
        var id = Guid.CreateVersion7();
        _store.FailWith = new InvalidOperationException("db down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(AssetOperation.Deposit, "PETR4", 100, id));

        Assert.DoesNotContain(id, _idempotency.Processed);
        Assert.Empty(_idempotency.Locked);
    }

    [Fact]
    public async Task Envelope_locked_by_another_worker_is_redelivered_not_dropped()
    {
        var id = Guid.CreateVersion7();
        _idempotency.Locked.Add(id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(AssetOperation.Deposit, "PETR4", 100, id));

        Assert.Empty(_store.Assets);
        Assert.Contains(id, _idempotency.Locked);
    }

    [Fact]
    public async Task Orphaned_state_without_key_is_reprocessed_and_the_durable_guard_prevents_double_apply()
    {
        var id = Guid.CreateVersion7();
        await Run(AssetOperation.Deposit, "PETR4", 100, id);
        _idempotency.Processed.Clear();
        _store.DuplicateEnvelope = true;

        await Run(AssetOperation.Deposit, "PETR4", 100, id);

        Assert.Equal(100, Assert.Single(_store.Assets.Values).Available.Value);
        Assert.Single(_idempotency.Processed);
    }

    [Fact]
    public async Task Persistent_concurrency_conflicts_end_as_an_infrastructure_failure()
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        _store.ConflictsLeft = int.MaxValue;
        var id = Guid.CreateVersion7();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(AssetOperation.Block, "PETR4", 10, id));

        Assert.DoesNotContain(id, _idempotency.Processed);
        Assert.Empty(_idempotency.Locked);
    }

    [Fact]
    public async Task Transient_concurrency_conflict_is_retried()
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        _store.ConflictsLeft = 2;

        await Run(AssetOperation.Block, "PETR4", 10);

        Assert.Equal(10, Assert.Single(_store.Assets.Values).Blocked.Value);
    }

    [Fact]
    public async Task Notifier_failure_does_not_fail_the_command()
    {
        _notifier.Fail = true;

        await Run(AssetOperation.Deposit, "PETR4", 100);

        Assert.Single(_store.Assets);
    }

    [Fact]
    public async Task Malformed_rejection_publishes_the_raw_body_and_marks_a_valid_id_as_processed()
    {
        var id = Guid.CreateVersion7();

        await _processor.RejectMalformedAsync(id, new MalformedCommand(), "{ not json", default);
        await _processor.RejectMalformedAsync(id, new MalformedCommand(), "{ not json", default);

        var failure = Assert.Single(_responses.Failures);
        Assert.Equal(nameof(MalformedCommand), failure.ErrorType);
        Assert.Equal("{ not json", failure.RawMessage);
        Assert.Equal(id, failure.EnvelopeId);
        Assert.Contains(id, _idempotency.Processed);
    }

    [Fact]
    public async Task Malformed_rejection_of_an_invalid_id_is_published_without_the_lease()
    {
        await _processor.RejectMalformedAsync(Guid.Empty, new MalformedCommand(), "x", default);
        await _processor.RejectMalformedAsync(Guid.NewGuid(), new InvalidEnvelopeId(), "y", default);

        Assert.Equal(2, _responses.Failures.Count);
        Assert.Empty(_idempotency.Processed);
    }

    [Fact]
    public async Task Sell_order_blocks_the_quantity_and_answers_with_the_order_id()
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        var orderId = Guid.CreateVersion7();

        await Sell(40, orderId);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
        var blocked = Assert.Single(_store.ReservationResponses).ResultValue;
        Assert.Equal(orderId, blocked.OrderId);
        Assert.Equal(_account, blocked.AccountId);
        Assert.Equal("PETR4", blocked.Ticker);
        Assert.Equal(40, blocked.Quantity);
        Assert.Equal(40, _store.Reservations[orderId].Remaining);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Sell_order_failures_are_queued_with_a_typed_kind()
    {
        await Run(AssetOperation.Deposit, "PETR4", 10);
        var orderId = Guid.CreateVersion7();

        await Sell(40, orderId);
        await Sell(1, Guid.CreateVersion7(), "VALE3");
        await Sell(0, Guid.CreateVersion7());

        var errors = _store.ReservationResponses.Select(r => r.ErrorValue).ToList();
        Assert.Equal(
            [AssetBlockErrorKind.InsufficientQuantity, AssetBlockErrorKind.AssetNotFound, AssetBlockErrorKind.InvalidOrder],
            errors.Select(e => e.Kind));
        Assert.Equal(orderId, errors[0].OrderId);
        Assert.Empty(_store.Reservations);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Duplicated_sell_order_does_not_block_twice()
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        var orderId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        await Sell(40, orderId, id: id);
        await Sell(40, orderId, id: id);
        _idempotency.Processed.Clear();
        await Sell(40, orderId, id: id);

        Assert.Equal(40, Assert.Single(_store.Assets.Values).Blocked.Value);
        Assert.Single(_store.ReservationResponses);
    }

    [Fact]
    public async Task Partial_fill_withdraws_the_executed_quantity_and_the_final_fill_unblocks_the_leftover()
    {
        var orderId = await ReserveAsync(40);

        await Settle(orderId, 10, final: false);
        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(30, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
        Assert.Equal(30, _store.Reservations[orderId].Remaining);
        Assert.False(_store.Reservations[orderId].Closed);

        await Settle(orderId, 20, final: true);
        asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(70, asset.Available.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Complete_fill_of_the_whole_quantity_closes_the_reservation_without_leftover()
    {
        var orderId = await ReserveAsync(40);

        await Settle(orderId, 40, final: true);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Closure_unblocks_what_is_left_and_later_events_are_ignored()
    {
        var orderId = await ReserveAsync(40);
        await Settle(orderId, 15, final: false);

        await Settle(orderId, 0, final: true);
        await Settle(orderId, 0, final: true);
        await Settle(orderId, 5, final: true);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(85, asset.Available.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Executed_quantity_is_limited_to_the_remaining_one()
    {
        var orderId = await ReserveAsync(40);

        await Settle(orderId, 500, final: false);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Duplicated_settlement_is_applied_once()
    {
        var orderId = await ReserveAsync(40);
        var id = Guid.CreateVersion7();

        await Settle(orderId, 10, final: false, id);
        _idempotency.Processed.Clear();
        await Settle(orderId, 10, final: false, id);

        Assert.Equal(30, Assert.Single(_store.Assets.Values).Blocked.Value);
    }

    [Fact]
    public async Task Fill_before_the_reservation_is_redelivered_a_few_times_and_then_discarded()
    {
        var orderId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        for (var attempt = 1; attempt < PortfolioCommandProcessor.MaxSettleAttempts; attempt++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => Settle(orderId, 10, final: true, id));

        await Settle(orderId, 10, final: true, id);
        Assert.Empty(_idempotency.Locked);
        Assert.Empty(_store.Assets);
    }

    private async Task<Guid> ReserveAsync(long quantity)
    {
        await Run(AssetOperation.Deposit, "PETR4", 100);
        var orderId = Guid.CreateVersion7();
        await Sell(quantity, orderId);
        return orderId;
    }
}
