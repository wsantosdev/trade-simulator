using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Events;

public sealed record OrderCancelRejected(OrderId OrderId) : OrderEvent(OrderId);
