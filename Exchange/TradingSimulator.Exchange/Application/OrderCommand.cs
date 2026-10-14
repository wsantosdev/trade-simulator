using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

// Commands received from the Orders module, already converted to Exchange types.
public abstract record OrderCommand(OrderId OrderId);

public sealed record SubmitOrder(
    OrderId OrderId, Ticker Ticker, OrderSide Side, OrderType Type, Quantity Quantity, Price? Price) : OrderCommand(OrderId);

// A submission whose fields are invalid: the order is identifiable, so it is rejected instead of dropped.
public sealed record RejectOrder(OrderId OrderId) : OrderCommand(OrderId);

public sealed record CancelOrder(OrderId OrderId) : OrderCommand(OrderId);

public sealed record ReplaceOrder(OrderId OrderId, Price NewPrice) : OrderCommand(OrderId);
