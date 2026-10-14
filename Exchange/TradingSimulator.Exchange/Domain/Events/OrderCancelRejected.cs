using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderCancelRejected(OrderId OrderId) : OrderEvent(OrderId);
