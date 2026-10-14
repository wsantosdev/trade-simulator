using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderAcked(OrderId OrderId) : OrderEvent(OrderId);
