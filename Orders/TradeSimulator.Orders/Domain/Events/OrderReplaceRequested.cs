using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

public sealed record OrderReplaceRequested(OrderId OrderId, decimal NewPrice, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
