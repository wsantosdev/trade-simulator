using PortfolioResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Application.PortfolioCommandResult, TradingSimulator.Portfolio.Application.PortfolioCommandError>>;
using AssetReservationResponse = TradingSimulator.Commons.EventEnvelope<Moonad.Result<TradingSimulator.Portfolio.Application.AssetsBlocked, TradingSimulator.Portfolio.Application.AssetBlockError>>;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Moonad;
using TradingSimulator.Portfolio.Application;
using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Tests;

public sealed class PortfolioCommandProcessorTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly FakeStore _store = new();
    private readonly FakeIdempotency _idempotency = new();
    private readonly FakeResponses _responses = new();
    private readonly FakeNotifier _notifier = new();
    private readonly PortfolioCommandProcessor _processor;
    private readonly Guid _account = Guid.CreateVersion7();

    public PortfolioCommandProcessorTests() =>
        _processor = new(_store, _idempotency, _responses, _notifier, TimeProvider.System, NullLogger<PortfolioCommandProcessor>.Instance);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string Envelope(string type, object payload, Guid? id = null) =>
        JsonSerializer.Serialize(new EventEnvelope<object>(id ?? Guid.CreateVersion7(), type, payload), Options);

    private string Command(string type, string ticker, long quantity, Guid? id = null) =>
        Envelope(type, new { accountId = _account, ticker, quantity }, id);

    [Fact]
    public async Task First_deposit_creates_the_asset_and_the_next_one_increments_it()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 50), Options, default);

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
        await _processor.ProcessCommandAsync(Command("Deposit", "petr4", 100, id), Options, default);
        await _processor.ProcessCommandAsync(Command("Block", "PETR4", 30), Options, default);

        var result = _store.Responses[1].Event.ResultValue;
        Assert.Equal(30, result.RequestedQuantity);
        Assert.Equal(70, result.Available);
        Assert.Equal(30, result.Blocked);
        Assert.Equal(100, result.Total);
        Assert.Equal(id, _store.Responses[0].Event.ResultValue.EnvelopeId);
    }

    [Fact]
    public async Task Withdraw_of_everything_leaves_a_zero_total_and_frees_the_slot()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        await _processor.ProcessCommandAsync(Command("Block", "PETR4", 100), Options, default);
        await _processor.ProcessCommandAsync(Command("Withdraw", "PETR4", 100), Options, default);

        Assert.Equal(0, _store.Responses[^1].Event.ResultValue.Total);
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 10), Options, default);
        Assert.Equal(2, _store.Assets.Count);
    }

    [Fact]
    public async Task Unblock_above_blocked_is_a_typed_business_failure_and_keeps_the_key()
    {
        var id = Guid.CreateVersion7();
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        await _processor.ProcessCommandAsync(Command("Unblock", "PETR4", 10, id), Options, default);

        var failure = Assert.Single(_responses.Failures).Event.ErrorValue;
        Assert.Equal(nameof(InsufficientBlockedQuantity), failure.ErrorType);
        Assert.Equal(10, failure.RequestedQuantity);
        Assert.Equal(100, failure.Available);
        Assert.Contains(id, _idempotency.Processed);
        Assert.Empty(_idempotency.Locked);
    }

    [Fact]
    public async Task Command_on_unknown_asset_fails_with_asset_not_found()
    {
        await _processor.ProcessCommandAsync(Command("Block", "PETR4", 10), Options, default);

        Assert.Equal(nameof(AssetNotFound), Assert.Single(_responses.Failures).Event.ErrorValue.ErrorType);
    }

    [Theory]
    [InlineData("XX", 10, nameof(InvalidTicker))]
    [InlineData("PETR4", 0, nameof(InvalidQuantity))]
    [InlineData("PETR4", 1_000_001, nameof(QuantityAboveLimit))]
    public async Task Invalid_values_fail_with_typed_errors(string ticker, long quantity, string expected)
    {
        await _processor.ProcessCommandAsync(Command("Deposit", ticker, quantity), Options, default);

        Assert.Equal(expected, Assert.Single(_responses.Failures).Event.ErrorValue.ErrorType);
        Assert.Empty(_store.Assets);
    }

    [Fact]
    public async Task Malformed_json_returns_a_typed_error_carrying_the_raw_body()
    {
        await _processor.ProcessCommandAsync("{ not json", Options, default);

        var failure = Assert.Single(_responses.Failures).Event.ErrorValue;
        Assert.Equal(nameof(MalformedCommand), failure.ErrorType);
        Assert.Equal("{ not json", failure.RawMessage);
    }

    [Fact]
    public async Task Invalid_payload_returns_malformed_command()
    {
        await _processor.ProcessCommandAsync(Envelope("Deposit", new { accountId = "nope", quantity = "x" }), Options, default);

        Assert.Equal(nameof(MalformedCommand), Assert.Single(_responses.Failures).Event.ErrorValue.ErrorType);
    }

    [Fact]
    public async Task Invalid_envelope_id_and_unsupported_command_are_rejected()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 1, Guid.NewGuid()), Options, default);
        await _processor.ProcessCommandAsync(Command("Explode", "PETR4", 1), Options, default);

        Assert.Equal(nameof(InvalidEnvelopeId), _responses.Failures[0].Event.ErrorValue.ErrorType);
        Assert.Equal(nameof(UnsupportedCommand), _responses.Failures[1].Event.ErrorValue.ErrorType);
    }

    [Fact]
    public async Task Duplicated_envelope_is_discarded()
    {
        var body = Command("Deposit", "PETR4", 100);
        await _processor.ProcessCommandAsync(body, Options, default);
        await _processor.ProcessCommandAsync(body, Options, default);

        Assert.Equal(100, Assert.Single(_store.Assets.Values).Available.Value);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Infrastructure_failure_never_writes_the_key_releases_the_lock_and_rethrows()
    {
        var id = Guid.CreateVersion7();
        _store.FailWith = new InvalidOperationException("db down");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100, id), Options, default));

        Assert.DoesNotContain(id, _idempotency.Processed);
        Assert.Empty(_idempotency.Locked);
    }

    [Fact]
    public async Task Envelope_locked_by_another_worker_is_redelivered_not_dropped()
    {
        var id = Guid.CreateVersion7();
        _idempotency.Locked.Add(id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100, id), Options, default));

        Assert.Empty(_store.Assets);
        Assert.Contains(id, _idempotency.Locked);
    }

    [Fact]
    public async Task Orphaned_state_without_key_is_reprocessed_and_the_durable_guard_prevents_double_apply()
    {
        var body = Command("Deposit", "PETR4", 100);
        await _processor.ProcessCommandAsync(body, Options, default);
        _idempotency.Processed.Clear();
        _store.DuplicateEnvelope = true;

        await _processor.ProcessCommandAsync(body, Options, default);

        Assert.Equal(100, Assert.Single(_store.Assets.Values).Available.Value);
        Assert.Single(_idempotency.Processed);
    }

    [Fact]
    public async Task Persistent_concurrency_conflicts_end_as_an_infrastructure_failure()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        _store.ConflictsLeft = int.MaxValue;
        var id = Guid.CreateVersion7();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _processor.ProcessCommandAsync(Command("Block", "PETR4", 10, id), Options, default));

        Assert.DoesNotContain(id, _idempotency.Processed);
        Assert.Empty(_idempotency.Locked);
    }

    [Fact]
    public async Task Transient_concurrency_conflict_is_retried()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        _store.ConflictsLeft = 2;

        await _processor.ProcessCommandAsync(Command("Block", "PETR4", 10), Options, default);

        Assert.Equal(10, Assert.Single(_store.Assets.Values).Blocked.Value);
    }

    [Fact]
    public async Task Notifier_failure_does_not_fail_the_command()
    {
        _notifier.Fail = true;

        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);

        Assert.Single(_store.Assets);
    }

    [Fact]
    public async Task Sell_order_submitted_blocks_the_quantity_and_buy_order_is_discarded()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);

        await _processor.ProcessOrderEventAsync(OrderSent("Buy", 40), Options, default);
        Assert.Equal(0, Assert.Single(_store.Assets.Values).Blocked.Value);

        await _processor.ProcessOrderEventAsync(OrderSent("Sell", 40), Options, default);
        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
    }

    [Fact]
    public async Task Sell_order_above_available_fails_and_other_order_events_are_ignored()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 10), Options, default);

        await _processor.ProcessOrderEventAsync(OrderSent("Sell", 40), Options, default);
        await _processor.ProcessOrderEventAsync(Envelope("OrderAcked", new { orderId = Guid.CreateVersion7() }), Options, default);

        Assert.Equal(AssetBlockErrorKind.InsufficientQuantity, Assert.Single(_store.ReservationResponses).Event.ErrorValue.Kind);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Sell_order_submitted_answers_with_an_asset_reservation_carrying_the_order_id()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        var orderId = Guid.CreateVersion7();

        await _processor.ProcessOrderEventAsync(OrderSubmitted("Sell", 40, orderId), Options, default);

        var blocked = Assert.Single(_store.ReservationResponses).Event.ResultValue;
        Assert.Equal(orderId, blocked.OrderId);
        Assert.Equal(_account, blocked.AccountId);
        Assert.Equal("PETR4", blocked.Ticker);
        Assert.Equal(40, blocked.Quantity);
        Assert.Equal(40, _store.Reservations[orderId].Remaining);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Sell_order_submitted_failures_are_queued_with_a_typed_kind()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 10), Options, default);
        var orderId = Guid.CreateVersion7();

        await _processor.ProcessOrderEventAsync(OrderSubmitted("Sell", 40, orderId), Options, default);
        await _processor.ProcessOrderEventAsync(OrderSubmitted("Sell", 1, Guid.CreateVersion7(), "VALE3"), Options, default);
        await _processor.ProcessOrderEventAsync(OrderSubmitted("Sell", 0, Guid.CreateVersion7()), Options, default);

        var errors = _store.ReservationResponses.Select(r => r.Event.ErrorValue).ToList();
        Assert.Equal(
            [AssetBlockErrorKind.InsufficientQuantity, AssetBlockErrorKind.AssetNotFound, AssetBlockErrorKind.InvalidOrder],
            errors.Select(e => e.Kind));
        Assert.Equal(orderId, errors[0].OrderId);
        Assert.Empty(_store.Reservations);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Duplicated_order_submitted_does_not_block_twice()
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        var message = OrderSubmitted("Sell", 40, Guid.CreateVersion7(), id: Guid.CreateVersion7());

        await _processor.ProcessOrderEventAsync(message, Options, default);
        await _processor.ProcessOrderEventAsync(message, Options, default);
        _idempotency.Processed.Clear();
        await _processor.ProcessOrderEventAsync(message, Options, default);

        Assert.Equal(40, Assert.Single(_store.Assets.Values).Blocked.Value);
        Assert.Single(_store.ReservationResponses);
    }

    [Fact]
    public async Task Partial_fill_withdraws_the_executed_quantity_and_the_final_fill_unblocks_the_leftover()
    {
        var orderId = await ReserveAsync(40);

        await _processor.ProcessExchangeEventAsync(Exchange("OrderPartiallyFilled", orderId, 10), Options, default);
        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(30, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
        Assert.Equal(30, _store.Reservations[orderId].Remaining);
        Assert.False(_store.Reservations[orderId].Closed);

        await _processor.ProcessExchangeEventAsync(Exchange("OrderFilled", orderId, 20), Options, default);
        asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(70, asset.Available.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Complete_fill_of_the_whole_quantity_closes_the_reservation_without_leftover()
    {
        var orderId = await ReserveAsync(40);

        await _processor.ProcessExchangeEventAsync(Exchange("OrderFilled", orderId, 40), Options, default);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Theory]
    [InlineData("OrderCancelled")]
    [InlineData("OrderRejected")]
    public async Task Cancel_and_reject_unblock_what_is_left_and_later_events_are_ignored(string type)
    {
        var orderId = await ReserveAsync(40);
        await _processor.ProcessExchangeEventAsync(Exchange("OrderPartiallyFilled", orderId, 15), Options, default);

        await _processor.ProcessExchangeEventAsync(Envelope(type, new { orderId }), Options, default);
        await _processor.ProcessExchangeEventAsync(Envelope(type, new { orderId }), Options, default);
        await _processor.ProcessExchangeEventAsync(Exchange("OrderFilled", orderId, 5), Options, default);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(85, asset.Available.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Executed_quantity_is_limited_to_the_remaining_one()
    {
        var orderId = await ReserveAsync(40);

        await _processor.ProcessExchangeEventAsync(Exchange("OrderPartiallyFilled", orderId, 500), Options, default);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(60, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Duplicated_exchange_event_is_applied_once()
    {
        var orderId = await ReserveAsync(40);
        var message = Exchange("OrderPartiallyFilled", orderId, 10, Guid.CreateVersion7());

        await _processor.ProcessExchangeEventAsync(message, Options, default);
        _idempotency.Processed.Clear();
        await _processor.ProcessExchangeEventAsync(message, Options, default);

        Assert.Equal(30, Assert.Single(_store.Assets.Values).Blocked.Value);
    }

    [Fact]
    public async Task Fill_before_the_reservation_is_redelivered_a_few_times_and_then_discarded()
    {
        var message = Exchange("OrderFilled", Guid.CreateVersion7(), 10, Guid.CreateVersion7());

        for (var attempt = 1; attempt < PortfolioCommandProcessor.MaxSettleAttempts; attempt++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => _processor.ProcessExchangeEventAsync(message, Options, default));

        await _processor.ProcessExchangeEventAsync(message, Options, default);
        Assert.Empty(_idempotency.Locked);
        Assert.Empty(_store.Assets);
    }

    [Fact]
    public async Task Non_exchange_events_and_invalid_payloads_are_discarded()
    {
        await _processor.ProcessExchangeEventAsync(Envelope("OrderAcked", new { orderId = Guid.CreateVersion7() }), Options, default);
        await _processor.ProcessExchangeEventAsync(Envelope("OrderFilled", new { orderId = "nope" }), Options, default);
        await _processor.ProcessExchangeEventAsync("{ not json", Options, default);

        Assert.Empty(_responses.Failures);
        Assert.Empty(_store.ReservationResponses);
    }

    private async Task<Guid> ReserveAsync(long quantity)
    {
        await _processor.ProcessCommandAsync(Command("Deposit", "PETR4", 100), Options, default);
        var orderId = Guid.CreateVersion7();
        await _processor.ProcessOrderEventAsync(OrderSubmitted("Sell", quantity, orderId), Options, default);
        return orderId;
    }

    private string Exchange(string type, Guid orderId, long executed, Guid? id = null) =>
        Envelope(type, new { orderId, executedQuantity = executed, executionPrice = 10m }, id);

    private string OrderSent(string side, long quantity) => OrderSubmitted(side, quantity, Guid.CreateVersion7());

    private string OrderSubmitted(string side, long quantity, Guid orderId, string ticker = "PETR4", Guid? id = null) =>
        Envelope("OrderSubmitted", new { orderId, accountId = _account, ticker, side, quantity }, id);

    private sealed class FakeStore : IAssetStore
    {
        public Dictionary<AssetId, Asset> Assets { get; } = [];
        public List<PortfolioResponse> Responses { get; } = [];
        public Exception? FailWith { get; set; }
        public int ConflictsLeft { get; set; }
        public bool DuplicateEnvelope { get; set; }
        public Dictionary<Guid, OrderReservation> Reservations { get; } = [];
        public List<AssetReservationResponse> ReservationResponses { get; } = [];
        public HashSet<Guid> SeenEnvelopes { get; } = [];

        public Task<Result<Asset, IPortfolioError>> ReserveAsync(
            Guid envelopeId, Asset asset, OrderReservation reservation, AssetReservationResponse response, CancellationToken cancellationToken)
        {
            if (!SeenEnvelopes.Add(envelopeId) || Reservations.ContainsKey(reservation.OrderId))
                return Task.FromResult<Result<Asset, IPortfolioError>>(new DuplicateEnvelope());

            Assets[asset.Id] = Asset.Restore(asset.ToSnapshot(), []).ResultValue;
            Reservations[reservation.OrderId] = reservation;
            ReservationResponses.Add(response);
            asset.MarkCommitted();
            return Task.FromResult<Result<Asset, IPortfolioError>>(asset);
        }

        public Task<bool> RejectReservationAsync(Guid envelopeId, AssetReservationResponse failure, CancellationToken cancellationToken)
        {
            if (!SeenEnvelopes.Add(envelopeId))
                return Task.FromResult(false);
            ReservationResponses.Add(failure);
            return Task.FromResult(true);
        }

        public Task<OrderReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Reservations.GetValueOrDefault(orderId));

        public Task<Result<bool, IPortfolioError>> SettleAsync(
            Guid envelopeId, Asset? asset, Guid orderId, long expectedRemaining, long newRemaining, bool close, CancellationToken cancellationToken)
        {
            if (!SeenEnvelopes.Add(envelopeId))
                return Task.FromResult<Result<bool, IPortfolioError>>(new DuplicateEnvelope());

            if (asset is not null)
            {
                Assets[asset.Id] = Asset.Restore(asset.ToSnapshot(), []).ResultValue;
                asset.MarkCommitted();
            }

            Reservations[orderId] = Reservations[orderId] with { Remaining = newRemaining, Closed = close };
            return Task.FromResult<Result<bool, IPortfolioError>>(true);
        }

        public Task<Result<Asset, IPortfolioError>> FindAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
                throw FailWith;

            var active = Assets.Values.FirstOrDefault(a => a.AccountId == accountId && a.Ticker == ticker && !a.IsRemoved);
            return Task.FromResult<Result<Asset, IPortfolioError>>(active is null ? new AssetNotFound() : Asset.Restore(active.ToSnapshot(), []).ResultValue);
        }

        public Task<Result<Asset, IPortfolioError>> CreateAsync(Asset asset, PortfolioResponse response, CancellationToken cancellationToken) =>
            Save(asset, response);

        public Task<Result<Asset, IPortfolioError>> AppendAsync(Asset asset, PortfolioResponse response, CancellationToken cancellationToken) =>
            Save(asset, response);

        private Task<Result<Asset, IPortfolioError>> Save(Asset asset, PortfolioResponse response)
        {
            if (DuplicateEnvelope)
                return Task.FromResult<Result<Asset, IPortfolioError>>(new DuplicateEnvelope());

            if (ConflictsLeft > 0)
            {
                ConflictsLeft--;
                return Task.FromResult<Result<Asset, IPortfolioError>>(new ConcurrencyConflict());
            }

            Assets[asset.Id] = Asset.Restore(asset.ToSnapshot(), []).ResultValue;
            Responses.Add(response);
            return Task.FromResult<Result<Asset, IPortfolioError>>(asset);
        }
    }

    private sealed class FakeIdempotency : IIdempotencyStore
    {
        public HashSet<Guid> Processed { get; } = [];
        public HashSet<Guid> Locked { get; } = [];
        public int Disposed { get; set; }

        public Task<IEnvelopeLease?> TryLockAsync(Guid envelopeId, CancellationToken cancellationToken) =>
            Task.FromResult<IEnvelopeLease?>(Locked.Add(envelopeId) ? new Lease(this, envelopeId) : null);

        private sealed class Lease(FakeIdempotency owner, Guid id) : IEnvelopeLease
        {
            public Task<bool> WasProcessedAsync(CancellationToken cancellationToken) => Task.FromResult(owner.Processed.Contains(id));

            public Task MarkProcessedAsync(CancellationToken cancellationToken)
            {
                owner.Processed.Add(id);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                owner.Locked.Remove(id);
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeResponses : IResponsePublisher
    {
        public List<PortfolioResponse> Failures { get; } = [];

        public Task PublishFailureAsync(PortfolioResponse response, CancellationToken cancellationToken)
        {
            Failures.Add(response);
            return Task.CompletedTask;
        }

        public Task PublishPendingAsync(PendingResponse pending, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeNotifier : IAssetNotifier
    {
        public List<AssetItem> Notified { get; } = [];
        public bool Fail { get; set; }

        public Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken)
        {
            if (Fail)
                throw new InvalidOperationException("hub down");
            Notified.Add(asset);
            return Task.CompletedTask;
        }
    }
}
