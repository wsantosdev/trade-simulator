using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderCancelled(OrderId OrderId, Quantity Executed, Quantity Remaining) : OrderEvent(OrderId);
