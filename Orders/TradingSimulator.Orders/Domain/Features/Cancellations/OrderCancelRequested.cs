namespace TradingSimulator.Orders.Domain.Features.Cancellations;

public sealed record OrderCancelRequested(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
