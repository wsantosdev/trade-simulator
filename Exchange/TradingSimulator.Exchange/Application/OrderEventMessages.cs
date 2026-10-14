using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

// Exchange -> Orders events. Field meanings follow the Orders contract (ExchangeMessages.cs in the Orders module).
public sealed record OrderAckedMessage(OrderId OrderId);
public sealed record OrderRejectedMessage(OrderId OrderId);
public sealed record OrderPartiallyFilledMessage(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity, decimal ExecutionPrice);
public sealed record OrderFilledMessage(OrderId OrderId, long ExecutedQuantity, decimal ExecutionPrice);
public sealed record OrderCancelledMessage(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity);
public sealed record OrderReplacedMessage(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity);
public sealed record OrderCancelRejectedMessage(OrderId OrderId);
public sealed record OrderReplaceRejectedMessage(OrderId OrderId);
