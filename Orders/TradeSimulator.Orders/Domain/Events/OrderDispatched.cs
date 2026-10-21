using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderDispatched(
    OrderId OrderId,
    AccountId AccountId,
    Ticker Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    RequestHash RequestHash,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
