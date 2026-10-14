using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

public abstract record OrderEvent(OrderId OrderId, DateTimeOffset OccurredAt);
