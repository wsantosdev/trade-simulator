using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Tests;

public sealed class OrderChangesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Changes_carry_only_the_fields_that_were_altered()
    {
        var order = Order.Send(OrderId.New(), Guid.CreateVersion7(), "PETR4", OrderSide.Buy, OrderType.Limit,
            100, 10m, new string('a', 64), T0).ResultValue;
        order.Dispatch(T0);
        order.Ack(T0.AddSeconds(1));
        var before = OrderView.From(order);

        order.PartiallyFill(30, 70, 11m, T0.AddSeconds(2));
        var changes = OrderChanges.Between(before, OrderView.From(order));

        Assert.Equal(OrderState.PartiallyFilled, changes.State);
        Assert.Equal(30, changes.ExecutedQuantity);
        Assert.Equal(70, changes.RemainingQuantity);
        Assert.Equal(11m, changes.AveragePrice);
        Assert.Equal(11m, changes.LastPrice);
        Assert.Equal(T0.AddSeconds(2), changes.LastActionAt);
        Assert.Null(changes.Price);
    }

    [Fact]
    public void Unchanged_view_produces_no_field_changes()
    {
        var order = Order.Send(OrderId.New(), Guid.CreateVersion7(), "PETR4", OrderSide.Buy, OrderType.Market,
            100, null, new string('b', 64), T0).ResultValue;
        var view = OrderView.From(order);

        var changes = OrderChanges.Between(view, view);

        Assert.Null(changes.State);
        Assert.Null(changes.ExecutedQuantity);
        Assert.Null(changes.LastTradeAt);
    }
}
