using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderRejected(OrderId OrderId, DateTimeOffset OccurredAt, string? Reason = null) : OrderEvent(OrderId, OccurredAt);
