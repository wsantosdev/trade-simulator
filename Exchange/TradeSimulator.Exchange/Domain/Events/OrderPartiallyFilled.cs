using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderPartiallyFilled(OrderId OrderId, Quantity Executed, Quantity Remaining, Price ExecutionPrice)
    : OrderEvent(OrderId);
