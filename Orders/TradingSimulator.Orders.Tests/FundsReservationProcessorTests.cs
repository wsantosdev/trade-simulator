using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moonad;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.Events;
using TradingSimulator.Orders.Domain.ValueObjects;
using TradingSimulator.Orders.Infrastructure.Messaging;
using TradingSimulator.Orders.Infrastructure.Serialization;
using TradingSimulator.Orders.Processors.AssetReservation;
using TradingSimulator.Orders.Processors.OrderTransition;
using TradingSimulator.Orders.Processors.FundsReservation;

namespace TradingSimulator.Orders.Tests;

public sealed class FundsReservationProcessorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore(Order order) : IOrderStore
    {
        public Order Order { get; } = order;
        public int Appends;
        public List<OrderEvent> Appended { get; } = [];
        public Task<Result<Order, IOrderError>> SendAsync(Order o, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Order, IOrderError>> AppendAsync(Order o, CancellationToken ct)
        {
            Appends++;
            Appended.AddRange(o.UncommittedEvents);
            o.MarkCommitted();
            return Task.FromResult<Result<Order, IOrderError>>(o);
        }
        public Task<Result<Order, IOrderError>> GetAsync(OrderId id, CancellationToken ct) =>
            Task.FromResult<Result<Order, IOrderError>>(id == Order.Id ? Order : new OrderNotFound());
    }

    private sealed class FakeNotifier : IOrderNotifier
    {
        public int Calls;
        public Task OrderCreatedAsync(OrderView order, CancellationToken ct) => Task.CompletedTask;
        public Task OrderUpdatedAsync(Guid accountId, OrderChanges changes, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static Order NewOrder() =>
        Order.Send(OrderId.New(), Guid.CreateVersion7(), "PETR4", OrderSide.Buy, OrderType.Limit, 100, 10m, new string('a', 64), T0).ResultValue;

    private static string Wrap(string inner) =>
        "{\"id\":\"" + Guid.CreateVersion7() + "\",\"eventType\":\"FundsReservationResult\",\"event\":" + inner + "}";

    private static string ErrorBody(Order order, string kind = "InsufficientFunds") =>
        Wrap("{\"isError\":true,\"error\":{\"orderId\":\"" + order.Id.Guid + "\",\"accountId\":\"" + order.AccountId.Guid +
             "\",\"kind\":\"" + kind + "\",\"message\":\"x\"}}");

    private static string OkBody(Order order) =>
        Wrap("{\"isError\":false,\"result\":{\"orderId\":\"" + order.Id.Guid + "\",\"accountId\":\"" + order.AccountId.Guid +
             "\",\"amount\":1000.00}}");

    private static (IOrderTransitions Outcomes, FakeStore Store, FakeNotifier Notifier) Create(Order order)
    {
        var store = new FakeStore(order);
        var notifier = new FakeNotifier();
        return (new OrderTransitions(store, notifier, TimeProvider.System, NullLogger<OrderTransitions>.Instance), store, notifier);
    }

    private static string AssetErrorBody(Order order, string kind = "InsufficientQuantity") =>
        "{\"id\":\"" + Guid.CreateVersion7() + "\",\"eventType\":\"AssetReservationResult\",\"event\":{\"isError\":true,\"error\":{\"orderId\":\"" +
        order.Id.Guid + "\",\"accountId\":\"" + order.AccountId.Guid + "\",\"ticker\":\"PETR4\",\"kind\":\"" + kind + "\",\"message\":\"x\"}}}";

    private static string AssetOkBody(Order order) =>
        "{\"id\":\"" + Guid.CreateVersion7() + "\",\"eventType\":\"AssetReservationResult\",\"event\":{\"isError\":false,\"result\":{\"orderId\":\"" +
        order.Id.Guid + "\",\"accountId\":\"" + order.AccountId.Guid + "\",\"ticker\":\"PETR4\",\"quantity\":100}}}";

    private static Task Funds(Order order, string body, out FakeStore store, out FakeNotifier notifier)
    {
        var (outcomes, s, n) = Create(order);
        store = s;
        notifier = n;
        var processor = new FundsReservationProcessor(outcomes, new FakeMetrics(), NullLogger<FundsReservationProcessor>.Instance);
        return new FundsReservationMessageHandler(processor, NullLogger<FundsReservationMessageHandler>.Instance).HandleAsync(body, default);
    }

    private static Task Assets(Order order, string body, out FakeStore store, out FakeNotifier notifier)
    {
        var (outcomes, s, n) = Create(order);
        store = s;
        notifier = n;
        var processor = new AssetReservationProcessor(outcomes, new FakeMetrics(), NullLogger<AssetReservationProcessor>.Instance);
        return new AssetReservationMessageHandler(processor, NullLogger<AssetReservationMessageHandler>.Instance).HandleAsync(body, default);
    }

    [Fact]
    public async Task Funds_success_dispatches_pending_submission_order()
    {
        var order = NewOrder();

        await Funds(order, OkBody(order), out var store, out var notifier);

        Assert.Equal(OrderState.PendingNew, order.State);
        Assert.IsType<OrderDispatched>(store.Appended[^1]);
        Assert.Equal(1, store.Appends);
        Assert.Equal(1, notifier.Calls);
    }

    [Fact]
    public async Task Funds_error_rejects_locally_with_reason_without_cancel_request()
    {
        var order = NewOrder();

        await Funds(order, ErrorBody(order), out var store, out var notifier);

        Assert.Equal(OrderState.Rejected, order.State);
        Assert.Equal("InsufficientFunds", Assert.IsType<OrderRejected>(store.Appended[^1]).Reason);
        Assert.DoesNotContain(store.Appended, e => e is OrderCancelRequested or OrderDispatched);
        Assert.Equal(1, store.Appends);
        Assert.Equal(1, notifier.Calls);
    }

    [Fact]
    public async Task Asset_success_dispatches_and_error_rejects_with_kind()
    {
        var ok = NewOrder();
        await Assets(ok, AssetOkBody(ok), out _, out _);
        Assert.Equal(OrderState.PendingNew, ok.State);

        var failed = NewOrder();
        await Assets(failed, AssetErrorBody(failed, "AssetNotFound"), out var failedStore, out _);
        Assert.Equal(OrderState.Rejected, failed.State);
        Assert.Equal("AssetNotFound", Assert.IsType<OrderRejected>(failedStore.Appended[^1]).Reason);
    }

    [Fact]
    public async Task Duplicate_success_and_responses_for_other_states_are_ignored()
    {
        var order = NewOrder();
        order.Dispatch(T0.AddSeconds(1));
        order.Ack(T0.AddSeconds(2));
        var versionBefore = order.Version;

        await Funds(order, OkBody(order), out var store, out _);
        await Funds(order, ErrorBody(order), out var errorStore, out var notifier);
        await Assets(order, AssetOkBody(order), out var assetStore, out _);

        Assert.Equal(OrderState.New, order.State);
        Assert.Equal(versionBefore, order.Version);
        Assert.Equal(0, store.Appends + errorStore.Appends + assetStore.Appends);
        Assert.Equal(0, notifier.Calls);
    }

    [Fact]
    public async Task Malformed_and_unknown_order_messages_do_not_change_anything_nor_throw()
    {
        var order = NewOrder();
        var other = NewOrder();

        await Funds(order, "not json", out var store, out _);
        await Funds(order, """{"id":"x","eventType":"y","event":{"isError":true}}""", out _, out _);
        await Funds(order, ErrorBody(other), out var otherStore, out _);
        await Assets(order, "not json", out _, out _);
        await Assets(order, AssetOkBody(other), out var assetStore, out _);

        Assert.Equal(OrderState.PendingDispatching, order.State);
        Assert.Equal(0, store.Appends + otherStore.Appends + assetStore.Appends);
    }

    [Fact]
    public void Result_wire_formats_round_trip_through_order_json()
    {
        var order = NewOrder();
        var funds = JsonSerializer.Deserialize<EventEnvelope<Result<FundsReserved, FundsReservationError>>>(OkBody(order), OrderJson.Options)!;
        var assets = JsonSerializer.Deserialize<EventEnvelope<Result<AssetsBlocked, AssetBlockError>>>(AssetErrorBody(order), OrderJson.Options)!;

        Assert.False(funds.Event.IsError);
        Assert.Equal(1000m, funds.Event.ResultValue.Amount);
        Assert.True(assets.Event.IsError);
        Assert.Equal("InsufficientQuantity", assets.Event.ErrorValue.Kind);
    }
}
