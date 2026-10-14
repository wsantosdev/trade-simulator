using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderPartiallyFilled(OrderId OrderId, Quantity Executed, Quantity Remaining, Price ExecutionPrice)
    : OrderEvent(OrderId);
