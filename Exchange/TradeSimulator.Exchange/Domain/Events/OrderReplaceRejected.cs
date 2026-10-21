using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderReplaceRejected(OrderId OrderId) : OrderEvent(OrderId);
