using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderReplaced(OrderId OrderId, Quantity Executed, Quantity Remaining) : OrderEvent(OrderId);
