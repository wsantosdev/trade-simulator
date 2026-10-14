using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Processors.OrderTransition;

// What the exchange reports about an order, already converted to Orders types.
public abstract record ExchangeEvent(OrderId OrderId);

public sealed record ExchangeAcked(OrderId OrderId) : ExchangeEvent(OrderId);

public sealed record ExchangeRejected(OrderId OrderId) : ExchangeEvent(OrderId);

public sealed record ExchangePartiallyFilled(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity, decimal ExecutionPrice) : ExchangeEvent(OrderId);

public sealed record ExchangeFilled(OrderId OrderId, long ExecutedQuantity, decimal ExecutionPrice) : ExchangeEvent(OrderId);

public sealed record ExchangeCancelled(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity) : ExchangeEvent(OrderId);

public sealed record ExchangeReplaced(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity) : ExchangeEvent(OrderId);

public sealed record ExchangeCancelRejected(OrderId OrderId) : ExchangeEvent(OrderId);

public sealed record ExchangeReplaceRejected(OrderId OrderId) : ExchangeEvent(OrderId);
