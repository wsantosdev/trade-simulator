using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TradingSimulator.Commons;
using TradingSimulator.Portfolio.Domain;
using TradingSimulator.Portfolio.Infrastructure.Messaging;
using TradingSimulator.Portfolio.Infrastructure.Serialization;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradingSimulator.Portfolio.Tests.Infrastructure;

public sealed class PortfolioMessageHandlerTests
{
    private readonly FakeStore _store = new();
    private readonly FakeIdempotency _idempotency = new();
    private readonly FakeResponses _responses = new();
    private readonly PortfolioCommandMessageHandler _commands;
    private readonly OrderEventMessageHandler _orders;
    private readonly ExchangeEventMessageHandler _exchange;
    private readonly Guid _account = Guid.CreateVersion7();

    public PortfolioMessageHandlerTests()
    {
        var processor = new PortfolioCommandProcessor(
            _store, _idempotency, _responses, new FakeNotifier(), TimeProvider.System, NullLogger<PortfolioCommandProcessor>.Instance);
        _commands = new(processor, NullLogger<PortfolioCommandMessageHandler>.Instance);
        _orders = new(processor, NullLogger<OrderEventMessageHandler>.Instance);
        _exchange = new(processor, NullLogger<ExchangeEventMessageHandler>.Instance);
    }

    private static string Envelope(string type, object payload, Guid? id = null) =>
        JsonSerializer.Serialize(new EventEnvelope<object>(id ?? Guid.CreateVersion7(), type, payload), PortfolioJson.Options);

    private string Command(string type, string ticker, long quantity, Guid? id = null) =>
        Envelope(type, new { accountId = _account, ticker, quantity }, id);

    private string OrderSubmitted(string side, long quantity, Guid orderId, string ticker = "PETR4", Guid? id = null) =>
        Envelope("OrderSubmitted", new { orderId, accountId = _account, ticker, side, quantity }, id);

    private static string Fill(string type, Guid orderId, long executed, Guid? id = null) =>
        Envelope(type, new { orderId, executedQuantity = executed, executionPrice = 10m }, id);

    [Fact]
    public async Task Commands_are_parsed_and_applied()
    {
        await _commands.HandleAsync(Command("Deposit", "PETR4", 100), default);
        await _commands.HandleAsync(Command("Block", "PETR4", 30), default);
        await _commands.HandleAsync(Command("Unblock", "PETR4", 10), default);
        await _commands.HandleAsync(Command("Withdraw", "PETR4", 20), default);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(80, asset.Total.Value);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Malformed_json_returns_a_typed_error_carrying_the_raw_body()
    {
        await _commands.HandleAsync("{ not json", default);

        var failure = Assert.Single(_responses.Failures);
        Assert.Equal(nameof(MalformedCommand), failure.ErrorType);
        Assert.Equal("{ not json", failure.RawMessage);
    }

    [Fact]
    public async Task Invalid_payload_returns_malformed_command()
    {
        await _commands.HandleAsync(Envelope("Deposit", new { accountId = "nope", quantity = "x" }), default);

        Assert.Equal(nameof(MalformedCommand), Assert.Single(_responses.Failures).ErrorType);
    }

    [Fact]
    public async Task Missing_event_type_returns_malformed_command()
    {
        await _commands.HandleAsync(Envelope("", new { }), default);

        Assert.Equal(nameof(MalformedCommand), Assert.Single(_responses.Failures).ErrorType);
    }

    [Fact]
    public async Task Invalid_envelope_id_and_unsupported_command_are_rejected()
    {
        await _commands.HandleAsync(Command("Deposit", "PETR4", 1, Guid.NewGuid()), default);
        await _commands.HandleAsync(Command("Explode", "PETR4", 1), default);

        Assert.Equal(nameof(InvalidEnvelopeId), _responses.Failures[0].ErrorType);
        Assert.Equal(nameof(UnsupportedCommand), _responses.Failures[1].ErrorType);
        Assert.Empty(_store.Assets);
    }

    [Fact]
    public async Task Duplicated_command_is_discarded()
    {
        var body = Command("Deposit", "PETR4", 100);
        await _commands.HandleAsync(body, default);
        await _commands.HandleAsync(body, default);

        Assert.Equal(100, Assert.Single(_store.Assets.Values).Available.Value);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Sell_order_submitted_blocks_the_quantity_and_buy_order_is_discarded()
    {
        await _commands.HandleAsync(Command("Deposit", "PETR4", 100), default);

        await _orders.HandleAsync(OrderSubmitted("Buy", 40, Guid.CreateVersion7()), default);
        Assert.Equal(0, Assert.Single(_store.Assets.Values).Blocked.Value);

        var orderId = Guid.CreateVersion7();
        await _orders.HandleAsync(OrderSubmitted("Sell", 40, orderId), default);
        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(60, asset.Available.Value);
        Assert.Equal(orderId, Assert.Single(_store.ReservationResponses).ResultValue.OrderId);
    }

    [Fact]
    public async Task Sell_order_above_available_fails_and_other_order_events_are_ignored()
    {
        await _commands.HandleAsync(Command("Deposit", "PETR4", 10), default);

        await _orders.HandleAsync(OrderSubmitted("Sell", 40, Guid.CreateVersion7()), default);
        await _orders.HandleAsync(Envelope("OrderAcked", new { orderId = Guid.CreateVersion7() }), default);

        Assert.Equal(AssetBlockErrorKind.InsufficientQuantity, Assert.Single(_store.ReservationResponses).ErrorValue.Kind);
        Assert.Empty(_responses.Failures);
    }

    [Fact]
    public async Task Invalid_order_messages_are_discarded_without_answers()
    {
        await _orders.HandleAsync("{ not json", default);
        await _orders.HandleAsync(Envelope("OrderSubmitted", new { orderId = "nope" }), default);
        await _orders.HandleAsync(OrderSubmitted("Sell", 10, Guid.CreateVersion7(), id: Guid.NewGuid()), default);

        Assert.Empty(_responses.Failures);
        Assert.Empty(_store.ReservationResponses);
    }

    [Fact]
    public async Task Duplicated_order_submitted_does_not_block_twice()
    {
        await _commands.HandleAsync(Command("Deposit", "PETR4", 100), default);
        var message = OrderSubmitted("Sell", 40, Guid.CreateVersion7(), id: Guid.CreateVersion7());

        await _orders.HandleAsync(message, default);
        await _orders.HandleAsync(message, default);
        _idempotency.Processed.Clear();
        await _orders.HandleAsync(message, default);

        Assert.Equal(40, Assert.Single(_store.Assets.Values).Blocked.Value);
        Assert.Single(_store.ReservationResponses);
    }

    [Fact]
    public async Task Fills_and_closures_settle_the_reservation()
    {
        var orderId = await ReserveAsync(40);

        await _exchange.HandleAsync(Fill("OrderPartiallyFilled", orderId, 10), default);
        Assert.Equal(30, Assert.Single(_store.Assets.Values).Blocked.Value);
        Assert.False(_store.Reservations[orderId].Closed);

        await _exchange.HandleAsync(Fill("OrderFilled", orderId, 20), default);
        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(70, asset.Available.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Theory]
    [InlineData("OrderCancelled")]
    [InlineData("OrderRejected")]
    public async Task Cancel_and_reject_unblock_what_is_left_and_later_events_are_ignored(string type)
    {
        var orderId = await ReserveAsync(40);
        await _exchange.HandleAsync(Fill("OrderPartiallyFilled", orderId, 15), default);

        await _exchange.HandleAsync(Envelope(type, new { orderId }), default);
        await _exchange.HandleAsync(Envelope(type, new { orderId }), default);
        await _exchange.HandleAsync(Fill("OrderFilled", orderId, 5), default);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(0, asset.Blocked.Value);
        Assert.Equal(85, asset.Available.Value);
        Assert.True(_store.Reservations[orderId].Closed);
    }

    [Fact]
    public async Task Negative_executed_quantity_is_treated_as_zero()
    {
        var orderId = await ReserveAsync(40);

        await _exchange.HandleAsync(Fill("OrderPartiallyFilled", orderId, -5), default);

        var asset = Assert.Single(_store.Assets.Values);
        Assert.Equal(40, asset.Blocked.Value);
        Assert.Equal(100, asset.Total.Value);
    }

    [Fact]
    public async Task Non_exchange_events_and_invalid_messages_are_discarded()
    {
        await _exchange.HandleAsync(Envelope("OrderAcked", new { orderId = Guid.CreateVersion7() }), default);
        await _exchange.HandleAsync(Envelope("OrderFilled", new { orderId = "nope" }), default);
        await _exchange.HandleAsync(Envelope("OrderCancelled", new { orderId = Guid.Empty }), default);
        await _exchange.HandleAsync(Fill("OrderFilled", Guid.CreateVersion7(), 1, Guid.NewGuid()), default);
        await _exchange.HandleAsync("{ not json", default);

        Assert.Empty(_responses.Failures);
        Assert.Empty(_store.ReservationResponses);
        Assert.Empty(_idempotency.Processed);
    }

    private async Task<Guid> ReserveAsync(long quantity)
    {
        await _commands.HandleAsync(Command("Deposit", "PETR4", 100), default);
        var orderId = Guid.CreateVersion7();
        await _orders.HandleAsync(OrderSubmitted("Sell", quantity, orderId), default);
        return orderId;
    }
}
