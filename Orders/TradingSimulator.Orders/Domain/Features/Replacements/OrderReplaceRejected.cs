namespace TradingSimulator.Orders.Domain.Features.Replacements;

public sealed record OrderReplaceRejected(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
