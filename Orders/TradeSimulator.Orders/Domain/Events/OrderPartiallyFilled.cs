using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderPartiallyFilled(
    OrderId OrderId,
    long FilledQuantity,
    long RemainingQuantity,
    decimal ExecutionPrice,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
