using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderFilled(
    OrderId OrderId,
    long FilledQuantity,
    decimal ExecutionPrice,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
