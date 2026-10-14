using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderCancelled(OrderId OrderId, Quantity Executed, Quantity Remaining) : OrderEvent(OrderId);
