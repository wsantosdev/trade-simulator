using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderFilled(
    OrderId OrderId,
    long FilledQuantity,
    decimal ExecutionPrice,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
