using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderFilled(OrderId OrderId, Quantity Executed, Price ExecutionPrice) : OrderEvent(OrderId);
