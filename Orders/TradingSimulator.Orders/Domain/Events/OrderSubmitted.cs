using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderSubmitted(
    OrderId OrderId,
    AccountId AccountId,
    Ticker Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    RequestHash RequestHash,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
