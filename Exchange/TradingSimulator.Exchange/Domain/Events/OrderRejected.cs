using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

public sealed record OrderRejected(OrderId OrderId, string? Reason = null) : OrderEvent(OrderId);
