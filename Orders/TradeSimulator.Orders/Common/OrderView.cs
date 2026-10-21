using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Common;

public sealed record OrderView(
    Guid Id,
    Guid AccountId,
    string Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    OrderState State,
    long ExecutedQuantity,
    long RemainingQuantity,
    decimal? AveragePrice,
    decimal? LastPrice,
    DateTimeOffset? LastTradeAt,
    DateTimeOffset SentAt,
    DateTimeOffset LastActionAt)
{
    public static OrderView From(Order order) => new(
        order.Id.Guid,
        order.AccountId.Guid,
        order.Ticker.Value,
        order.Side,
        order.Type,
        order.Quantity,
        order.Price,
        order.State,
        order.ExecutedQuantity,
        order.RemainingQuantity,
        order.AveragePrice,
        order.LastPrice,
        order.LastTradeAt,
        order.SentAt,
        order.LastActionAt);
}

public sealed record OrderChanges(
    Guid Id,
    DateTimeOffset LastActionAt,
    OrderState? State,
    decimal? Price,
    long? ExecutedQuantity,
    long? RemainingQuantity,
    decimal? AveragePrice,
    decimal? LastPrice,
    DateTimeOffset? LastTradeAt)
{
    public static OrderChanges Between(OrderView before, OrderView after) => new(
        after.Id,
        after.LastActionAt,
        after.State != before.State ? after.State : null,
        after.Price != before.Price ? after.Price : null,
        after.ExecutedQuantity != before.ExecutedQuantity ? after.ExecutedQuantity : null,
        after.RemainingQuantity != before.RemainingQuantity ? after.RemainingQuantity : null,
        after.AveragePrice != before.AveragePrice ? after.AveragePrice : null,
        after.LastPrice != before.LastPrice ? after.LastPrice : null,
        after.LastTradeAt != before.LastTradeAt ? after.LastTradeAt : null);
}
