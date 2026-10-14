using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Tests;

public sealed class OrderTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Moonad.Result<Order, IOrderError> Send(
        string? ticker = "PETR4", OrderType type = OrderType.Limit, long quantity = 100,
        decimal? price = 10m, string? hash = null, OrderSide side = OrderSide.Buy) =>
        Order.Send(OrderId.New(), Guid.CreateVersion7(), ticker, side, type, quantity, price, hash ?? Hash, Now);

    [Fact]
    public void Limit_order_is_created_as_pending_new()
    {
        var result = Send();

        Assert.False(result.IsError);
        Assert.Equal(OrderState.PendingDispatching, result.ResultValue.State);
        Assert.Single(result.ResultValue.UncommittedEvents);
    }

    [Fact]
    public void Market_order_without_price_is_accepted_and_sell_is_allowed()
    {
        var result = Send(type: OrderType.Market, price: null, side: OrderSide.Sell);

        Assert.False(result.IsError);
        Assert.Null(result.ResultValue.Price);
    }

    [Fact]
    public void Market_order_with_price_is_rejected()
    {
        Assert.IsType<PriceNotAllowedForMarketOrder>(Send(type: OrderType.Market, price: 5m).ErrorValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Limit_order_requires_positive_price(double? price)
    {
        Assert.IsType<InvalidLimitPrice>(Send(price: price is null ? null : (decimal)price).ErrorValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Quantity_must_be_positive(long quantity)
    {
        Assert.IsType<InvalidQuantity>(Send(quantity: quantity).ErrorValue);
    }

    [Theory]
    [InlineData("PETR2")]
    [InlineData("PET4")]
    [InlineData("PETR44")]
    [InlineData("1ETR4")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_ticker_is_rejected(string? ticker)
    {
        Assert.IsType<InvalidTicker>(Send(ticker: ticker).ErrorValue);
    }

    [Fact]
    public void Ticker_is_normalized_to_uppercase()
    {
        Assert.Equal("VALE3", Send(ticker: " vale3 ").ResultValue.Ticker.ToString());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void Invalid_request_hash_is_rejected(string hash)
    {
        Assert.IsType<InvalidRequestHash>(Send(hash: hash).ErrorValue);
    }

    [Fact]
    public void Empty_account_is_rejected()
    {
        var result = Order.Send(OrderId.New(), Guid.Empty, "PETR4", OrderSide.Buy, OrderType.Limit, 1, 1m, Hash, Now);

        Assert.IsType<InvalidAccountId>(result.ErrorValue);
    }

    [Fact]
    public void Rehydrate_restores_the_sent_order()
    {
        var sent = Send().ResultValue;

        var restored = Order.Rehydrate(sent.UncommittedEvents);

        Assert.False(restored.IsError);
        Assert.Equal(sent.Id, restored.ResultValue.Id);
        Assert.Equal(sent.Price, restored.ResultValue.Price);
        Assert.Empty(restored.ResultValue.UncommittedEvents);
    }

    [Fact]
    public void Rehydrate_of_empty_history_fails()
    {
        Assert.True(Order.Rehydrate([]).IsError);
    }
}
