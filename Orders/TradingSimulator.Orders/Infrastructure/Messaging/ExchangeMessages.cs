using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed record OrderAckedMessage(OrderId OrderId);
public sealed record OrderRejectedMessage(OrderId OrderId, string? Reason = null);
public sealed record OrderPartiallyFilledMessage(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity, decimal ExecutionPrice);
public sealed record OrderFilledMessage(OrderId OrderId, long ExecutedQuantity, decimal ExecutionPrice);
public sealed record OrderCancelledMessage(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity);
public sealed record OrderReplacedMessage(OrderId OrderId, long ExecutedQuantity, long RemainingQuantity);
public sealed record OrderCancelRejectedMessage(OrderId OrderId);
public sealed record OrderReplaceRejectedMessage(OrderId OrderId);
