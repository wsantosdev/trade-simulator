using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderAcked(OrderId OrderId) : OrderEvent(OrderId);
