using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderScheduled(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
