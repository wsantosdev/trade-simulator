using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderFilled(OrderId OrderId, Quantity Executed, Price ExecutionPrice) : OrderEvent(OrderId);
