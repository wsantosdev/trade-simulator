using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderReplaceRejected(OrderId OrderId) : OrderEvent(OrderId);
