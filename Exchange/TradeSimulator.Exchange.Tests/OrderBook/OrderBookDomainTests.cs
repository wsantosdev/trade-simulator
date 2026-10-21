using TradeSimulator.Exchange.Domain.Entities;
using TradeSimulator.Exchange.Domain.Enums;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Tests.OrderBook;

public sealed class OrderBookDomainTests
{
    private static readonly Ticker Petr = Ticker.TryCreate("PETR4").ResultValue;

    private static Price P(decimal value) => Price.TryCreate(value).ResultValue;

    private static Quantity Q(long value) => Quantity.TryCreate(value).ResultValue;

    private static BookOrder Limit(OrderSide side, decimal price, long quantity, OrderOrigin origin = OrderOrigin.User) =>
        BookOrder.Limit(OrderId.New(), side, P(price), Q(quantity), origin);

    private static Exchange.Domain.Entities.OrderBook Book(params BookOrder[] orders)
    {
        var book = new Exchange.Domain.Entities.OrderBook(Petr);
        foreach (var order in orders)
            Assert.False(book.Submit(order).IsError);
        return book;
    }

    [Fact]
    public void Order_id_requires_uuid_v7_and_quantity_positive()
    {
        Assert.True(OrderId.TryCreate(Guid.NewGuid()).IsError);
        Assert.True(OrderId.TryCreate(Guid.CreateVersion7()).IsError is false);
        Assert.True(Quantity.TryCreate(0).IsError);
        Assert.True(Quantity.TryCreate(-1).IsError);
    }

    [Fact]
    public void Price_off_tick_is_rejected()
    {
        var book = new Exchange.Domain.Entities.OrderBook(Petr);
        Assert.True(book.Submit(Limit(OrderSide.Buy, 10.005m, 100)).IsError);
    }

    [Fact]
    public void Executes_at_resting_price_with_price_time_priority()
    {
        var first = Limit(OrderSide.Sell, 10.02m, 100, OrderOrigin.Simulated);
        var second = Limit(OrderSide.Sell, 10.02m, 100, OrderOrigin.Simulated);
        var better = Limit(OrderSide.Sell, 10.01m, 100, OrderOrigin.Simulated);
        var book = Book(first, second, better);

        var outcome = book.Submit(Limit(OrderSide.Buy, 10.05m, 250)).ResultValue;

        Assert.Equal([better.Id, first.Id, second.Id], outcome.Trades.Select(trade => trade.Resting.Id));
        Assert.Equal([10.01m, 10.02m, 10.02m], outcome.Trades.Select(trade => trade.Price.Value));
        Assert.Equal([100L, 100L, 50L], outcome.Trades.Select(trade => trade.Quantity.Value));
        Assert.Equal(50, second.Remaining.Value);
        Assert.False(outcome.Rested);
    }

    [Fact]
    public void Partial_fill_rests_remaining_on_own_side()
    {
        var book = Book(Limit(OrderSide.Sell, 10.01m, 100, OrderOrigin.Simulated));
        var buy = Limit(OrderSide.Buy, 10.01m, 300);

        var outcome = book.Submit(buy).ResultValue;

        Assert.True(outcome.Rested);
        Assert.Equal(200, buy.Remaining.Value);
        Assert.Equal((10.01m, 200L, 1), book.Levels(OrderSide.Buy, 10).Select(l => (l.Price.Value, l.Quantity.Value, l.Orders)).Single());
        Assert.Empty(book.Levels(OrderSide.Sell, 10));
    }

    [Fact]
    public void Limit_that_does_not_cross_rests()
    {
        var book = Book(Limit(OrderSide.Sell, 10.05m, 100));
        var outcome = book.Submit(Limit(OrderSide.Buy, 10.01m, 100)).ResultValue;
        Assert.Empty(outcome.Trades);
        Assert.True(outcome.Rested);
    }

    [Fact]
    public void Market_order_consumes_levels_and_cancels_the_balance()
    {
        var book = Book(
            Limit(OrderSide.Sell, 10.01m, 100, OrderOrigin.Simulated),
            Limit(OrderSide.Sell, 10.03m, 100, OrderOrigin.Simulated));
        var market = BookOrder.Market(OrderId.New(), OrderSide.Buy, Q(500), OrderOrigin.User);

        var outcome = book.Submit(market).ResultValue;

        Assert.Equal(2, outcome.Trades.Count);
        Assert.Equal(300, outcome.Unexecuted.Value);
        Assert.False(outcome.Rested);
        Assert.Equal(200, market.Executed.Value);
        Assert.Empty(book.Levels(OrderSide.Sell, 10));
        Assert.Empty(book.Levels(OrderSide.Buy, 10));
    }

    [Fact]
    public void Cancel_removes_resting_order_and_unknown_is_error()
    {
        var order = Limit(OrderSide.Buy, 10m, 100);
        var book = Book(order);

        Assert.Equal(order.Id, book.Cancel(order.Id).ResultValue.Id);
        Assert.Empty(book.Levels(OrderSide.Buy, 10));
        Assert.True(book.Cancel(order.Id).IsError);
    }

    [Fact]
    public void Replace_loses_priority()
    {
        var first = Limit(OrderSide.Buy, 10m, 100);
        var second = Limit(OrderSide.Buy, 10m, 100);
        var book = Book(first, second);

        Assert.False(book.Replace(first.Id, P(10m)).IsError);

        var outcome = book.Submit(Limit(OrderSide.Sell, 10m, 100)).ResultValue;
        Assert.Equal(second.Id, outcome.Trades.Single().Resting.Id);
    }

    [Fact]
    public void Replace_that_crosses_executes()
    {
        var resting = Limit(OrderSide.Sell, 10.05m, 100, OrderOrigin.Simulated);
        var buy = Limit(OrderSide.Buy, 10m, 100);
        var book = Book(resting, buy);

        var outcome = book.Replace(buy.Id, P(10.05m)).ResultValue;

        Assert.Equal(10.05m, outcome.Trades.Single().Price.Value);
        Assert.True(outcome.Trades.Single().AggressorRemaining.IsZero);
        Assert.False(book.IsResting(buy.Id));
    }

    [Fact]
    public void Levels_are_ordered_best_first_and_aggregated()
    {
        var book = Book(
            Limit(OrderSide.Buy, 9.98m, 100), Limit(OrderSide.Buy, 10m, 100), Limit(OrderSide.Buy, 10m, 50));
        var levels = book.Levels(OrderSide.Buy, 2);
        Assert.Equal([(10m, 150L, 2), (9.98m, 100L, 1)], levels.Select(l => (l.Price.Value, l.Quantity.Value, l.Orders)));
    }
}
