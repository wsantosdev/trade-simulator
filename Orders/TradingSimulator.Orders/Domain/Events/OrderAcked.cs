using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderAcked(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
