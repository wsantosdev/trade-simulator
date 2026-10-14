namespace TradingSimulator.Orders.Domain.Features.Acknowledgement;

public sealed record OrderAcked(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
