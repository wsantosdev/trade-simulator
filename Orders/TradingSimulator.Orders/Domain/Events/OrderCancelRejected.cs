using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderCancelRejected(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
