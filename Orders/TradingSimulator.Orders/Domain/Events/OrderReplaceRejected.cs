using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderReplaceRejected(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
