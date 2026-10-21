using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public abstract record OrderEvent(OrderId OrderId, DateTimeOffset OccurredAt);
