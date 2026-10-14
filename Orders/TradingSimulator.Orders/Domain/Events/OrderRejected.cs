using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderRejected(OrderId OrderId, DateTimeOffset OccurredAt, string? Reason = null) : OrderEvent(OrderId, OccurredAt);
