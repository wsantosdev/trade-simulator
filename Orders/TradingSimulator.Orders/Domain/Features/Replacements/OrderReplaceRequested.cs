namespace TradingSimulator.Orders.Domain.Features.Replacements;

public sealed record OrderReplaceRequested(OrderId OrderId, decimal NewPrice, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
