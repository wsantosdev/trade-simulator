using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.Events;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Tests;

public sealed class OrderLifecycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int seconds) => T0.AddSeconds(seconds);

    private static Order NewOrder(OrderType type = OrderType.Limit, long quantity = 100) =>
        Order.Send(OrderId.New(), Guid.CreateVersion7(), "PETR4", OrderSide.Buy, type, quantity,
            type == OrderType.Limit ? 10m : null, new string('a', 64), T0).ResultValue;

    private static Order Acked(OrderType type = OrderType.Limit)
    {
        var order = NewOrder(type);
        Assert.False(order.Dispatch(T0).IsError);
        Assert.False(order.Ack(At(1)).IsError);
        return order;
    }

    [Fact]
    public void Submitted_order_is_pending_submission_and_cannot_ack_cancel_or_replace()
    {
        var order = NewOrder();

        Assert.Equal(OrderState.PendingDispatching, order.State);
        Assert.IsType<InvalidStateTransition>(order.Ack(At(1)).ErrorValue);
        Assert.IsType<InvalidStateTransition>(order.RequestCancel(At(1)).ErrorValue);
        Assert.IsType<InvalidStateTransition>(order.RequestReplace(11m, At(1)).ErrorValue);
        Assert.IsType<InvalidStateTransition>(order.Reject(At(1)).ErrorValue);
    }

    [Fact]
    public void Dispatch_moves_pending_submission_to_pending_new_once()
    {
        var order = NewOrder();

        Assert.False(order.Dispatch(At(1)).IsError);

        Assert.Equal(OrderState.PendingNew, order.State);
        Assert.IsType<OrderDispatched>(order.UncommittedEvents[^1]);
        Assert.IsType<InvalidStateTransition>(order.Dispatch(At(2)).ErrorValue);
    }

    [Fact]
    public void RejectSubmission_rejects_locally_without_cancel_request_and_only_when_pending_submission()
    {
        var order = NewOrder();

        Assert.False(order.RejectSubmission("InsufficientFunds", At(1)).IsError);

        Assert.Equal(OrderState.Rejected, order.State);
        Assert.Equal("InsufficientFunds", Assert.IsType<OrderRejected>(order.UncommittedEvents[^1]).Reason);
        Assert.DoesNotContain(order.UncommittedEvents, e => e is OrderCancelRequested);
        Assert.IsType<InvalidStateTransition>(order.Dispatch(At(2)).ErrorValue);
        Assert.IsType<InvalidStateTransition>(Acked().RejectSubmission("x", At(2)).ErrorValue);
    }

    [Fact]
    public void Legacy_opening_rehydrates_as_pending_new_with_the_same_version()
    {
        var order = NewOrder();

        var restored = Order.Rehydrate(order.UncommittedEvents, legacyOpening: true).ResultValue;

        Assert.Equal(OrderState.PendingNew, restored.State);
        Assert.Equal(1, restored.Version);
        Assert.False(restored.Ack(At(1)).IsError);
    }

    [Fact]
    public void Ack_moves_pending_new_to_new_and_cannot_repeat()
    {
        var order = Acked();

        Assert.Equal(OrderState.New, order.State);
        Assert.IsType<InvalidStateTransition>(order.Ack(At(2)).ErrorValue);
    }

    [Fact]
    public void Reject_moves_pending_new_to_rejected()
    {
        var order = NewOrder();
        order.Dispatch(At(0));

        order.Reject(At(1));

        Assert.Equal(OrderState.Rejected, order.State);
    }

    [Fact]
    public void Partial_fills_track_quantities_average_and_last_price()
    {
        var order = Acked();

        order.PartiallyFill(30, 70, 10m, At(2));
        order.PartiallyFill(20, 50, 12m, At(3));

        Assert.Equal(OrderState.PartiallyFilled, order.State);
        Assert.Equal(50, order.RemainingQuantity);
        Assert.Equal(50, order.ExecutedQuantity);
        Assert.Equal(12m, order.LastPrice);
        Assert.Equal(At(3), order.LastTradeAt);
        Assert.Equal((30 * 10m + 20 * 12m) / 50, order.AveragePrice);
    }

    [Fact]
    public void Fill_completes_the_order()
    {
        var order = Acked();
        order.PartiallyFill(60, 40, 10m, At(2));

        order.Fill(40, 11m, At(3));

        Assert.Equal(OrderState.Filled, order.State);
        Assert.Equal(0, order.RemainingQuantity);
        Assert.Equal((60 * 10m + 40 * 11m) / 100, order.AveragePrice);
    }

    [Fact]
    public void Fill_before_ack_is_rejected()
    {
        Assert.IsType<InvalidStateTransition>(NewOrder().Fill(100, 10m, At(1)).ErrorValue);
    }

    [Fact]
    public void Invalid_partial_fill_is_rejected()
    {
        var order = Acked();

        Assert.IsType<InvalidExecutionQuantity>(order.PartiallyFill(0, 100, 10m, At(2)).ErrorValue);
        Assert.IsType<InvalidExecutionQuantity>(order.PartiallyFill(30, 60, 10m, At(2)).ErrorValue);
        Assert.IsType<InvalidExecutionQuantity>(order.Fill(50, 10m, At(2)).ErrorValue);
        Assert.IsType<InvalidExecutionPrice>(order.PartiallyFill(50, 50, 0m, At(2)).ErrorValue);
    }

    [Fact]
    public void Cancel_request_then_cancelled()
    {
        var order = Acked();

        order.RequestCancel(At(2));
        Assert.Equal(OrderState.PendingCancel, order.State);

        order.Cancel(0, 100, At(3));
        Assert.Equal(OrderState.Cancelled, order.State);
    }

    [Fact]
    public void Fill_during_pending_cancel_keeps_pending_until_cancel_is_rejected()
    {
        var order = Acked();
        order.RequestCancel(At(2));

        order.PartiallyFill(40, 60, 10m, At(3));
        Assert.Equal(OrderState.PendingCancel, order.State);
        Assert.Equal(60, order.RemainingQuantity);

        order.RejectCancel(At(4));
        Assert.Equal(OrderState.PartiallyFilled, order.State);
    }

    [Fact]
    public void Cancel_rejected_restores_new_state()
    {
        var order = Acked();
        order.RequestCancel(At(2));

        order.RejectCancel(At(3));

        Assert.Equal(OrderState.New, order.State);
    }

    [Fact]
    public void Full_fill_during_pending_cancel_ends_filled_and_late_rejection_is_recorded()
    {
        var order = Acked();
        order.RequestCancel(At(2));
        order.Fill(100, 10m, At(3));

        Assert.Equal(OrderState.Filled, order.State);
        Assert.False(order.RejectCancel(At(4)).IsError);
        Assert.Equal(OrderState.Filled, order.State);
    }

    [Fact]
    public void Cancel_request_is_rejected_for_pending_or_final_states()
    {
        Assert.IsType<InvalidStateTransition>(NewOrder().RequestCancel(At(1)).ErrorValue);

        var order = Acked();
        order.RequestCancel(At(2));
        Assert.IsType<InvalidStateTransition>(order.RequestCancel(At(3)).ErrorValue);
    }

    [Fact]
    public void Replace_applies_new_price_only_after_the_exchange_confirms()
    {
        var order = Acked();
        order.PartiallyFill(30, 70, 10m, At(2));

        order.RequestReplace(11m, At(3));
        Assert.Equal(OrderState.PendingReplace, order.State);
        Assert.Equal(10m, order.Price);

        order.Replace(30, 70, At(4));
        Assert.Equal(OrderState.PartiallyFilled, order.State);
        Assert.Equal(11m, order.Price);
    }

    [Fact]
    public void Replace_rejected_keeps_the_old_price()
    {
        var order = Acked();
        order.RequestReplace(11m, At(2));

        order.RejectReplace(At(3));

        Assert.Equal(OrderState.New, order.State);
        Assert.Equal(10m, order.Price);
    }

    [Fact]
    public void Market_order_cannot_be_replaced_and_price_must_be_positive()
    {
        Assert.IsType<ReplaceNotAllowedForMarketOrder>(Acked(OrderType.Market).RequestReplace(5m, At(2)).ErrorValue);
        Assert.IsType<InvalidLimitPrice>(Acked().RequestReplace(0m, At(2)).ErrorValue);
    }

    [Fact]
    public void Rehydrate_replays_the_full_history()
    {
        var order = Acked();
        order.PartiallyFill(40, 60, 10m, At(2));
        order.RequestReplace(12m, At(3));
        order.Replace(40, 60, At(4));

        var restored = Order.Rehydrate(order.UncommittedEvents).ResultValue;

        Assert.Equal(order.State, restored.State);
        Assert.Equal(order.Price, restored.Price);
        Assert.Equal(order.RemainingQuantity, restored.RemainingQuantity);
        Assert.Equal(order.AveragePrice, restored.AveragePrice);
        Assert.Equal(order.Version, restored.Version);
        Assert.Equal(At(4), restored.LastActionAt);
    }
}
