namespace TradingSimulator.Orders.Domain.Features.Fills;

public sealed record OrderFilled(
    OrderId OrderId,
    long FilledQuantity,
    decimal ExecutionPrice,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
