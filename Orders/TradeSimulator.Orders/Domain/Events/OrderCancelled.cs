using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderCancelled(
    OrderId OrderId,
    long ExecutedQuantity,
    long RemainingQuantity,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
