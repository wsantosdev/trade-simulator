using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderCancelled(
    OrderId OrderId,
    long ExecutedQuantity,
    long RemainingQuantity,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
