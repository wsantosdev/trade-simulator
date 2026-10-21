using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderRejected(OrderId OrderId, string? Reason = null) : OrderEvent(OrderId);
