using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Infrastructure.Http;

public sealed record OrderResponse(
    Guid Id,
    Guid AccountId,
    string Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    OrderState State,
    DateTimeOffset SentAt)
{
    public static OrderResponse From(Order order) => new(
        order.Id.Guid,
        order.AccountId.Guid,
        order.Ticker.Value,
        order.Side,
        order.Type,
        order.Quantity,
        order.Price,
        order.State,
        order.SentAt);
}
