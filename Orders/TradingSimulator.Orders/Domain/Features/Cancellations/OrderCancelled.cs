namespace TradingSimulator.Orders.Domain.Features.Cancellations;

public sealed record OrderCancelled(
    OrderId OrderId,
    long ExecutedQuantity,
    long RemainingQuantity,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
