using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public sealed record OrderReplaceRequested(OrderId OrderId, decimal NewPrice, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
