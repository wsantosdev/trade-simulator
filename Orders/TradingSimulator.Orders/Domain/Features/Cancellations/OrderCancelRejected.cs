namespace TradingSimulator.Orders.Domain.Features.Cancellations;

public sealed record OrderCancelRejected(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
