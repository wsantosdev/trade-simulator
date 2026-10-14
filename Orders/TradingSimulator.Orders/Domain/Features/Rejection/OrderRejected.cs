namespace TradingSimulator.Orders.Domain.Features.Rejection;

public sealed record OrderRejected(OrderId OrderId, DateTimeOffset OccurredAt, string? Reason = null) : OrderEvent(OrderId, OccurredAt);
