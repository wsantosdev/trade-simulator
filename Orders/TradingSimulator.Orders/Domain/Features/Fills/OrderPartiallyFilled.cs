namespace TradingSimulator.Orders.Domain.Features.Fills;

public sealed record OrderPartiallyFilled(
    OrderId OrderId,
    long FilledQuantity,
    long RemainingQuantity,
    decimal ExecutionPrice,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
