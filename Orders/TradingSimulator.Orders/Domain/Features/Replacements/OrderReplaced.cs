namespace TradingSimulator.Orders.Domain.Features.Replacements;

public sealed record OrderReplaced(
    OrderId OrderId,
    long ExecutedQuantity,
    long RemainingQuantity,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
