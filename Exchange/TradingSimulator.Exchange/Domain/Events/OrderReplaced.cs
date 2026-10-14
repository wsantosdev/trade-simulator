using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderReplaced(OrderId OrderId, Quantity Executed, Quantity Remaining) : OrderEvent(OrderId);
