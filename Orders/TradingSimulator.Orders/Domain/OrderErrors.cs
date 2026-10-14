namespace TradingSimulator.Orders.Domain;

public interface IOrderError;

public readonly struct InvalidOrderId : IOrderError;
public readonly struct InvalidAccountId : IOrderError;
public readonly struct InvalidTicker : IOrderError;
public readonly struct InvalidOrderSide : IOrderError;
public readonly struct InvalidOrderType : IOrderError;
public readonly struct InvalidQuantity : IOrderError;
public readonly struct InvalidLimitPrice : IOrderError;
public readonly struct PriceNotAllowedForMarketOrder : IOrderError;
public readonly struct InvalidRequestHash : IOrderError;
public readonly struct InvalidSortField : IOrderError;
public readonly struct InvalidStateTransition : IOrderError;
public readonly struct InvalidExecutionQuantity : IOrderError;
public readonly struct InvalidExecutionPrice : IOrderError;
public readonly struct ReplaceNotAllowedForMarketOrder : IOrderError;
public readonly struct ConcurrencyConflict : IOrderError;
public readonly struct OrderNotFound : IOrderError;
public readonly struct OrderAlreadyExists : IOrderError;
public readonly struct OrderOpeningEventMissing : IOrderError;
public readonly struct UnsupportedOrderEvent : IOrderError;
public readonly struct CorruptOrderHistory : IOrderError;
public readonly struct OrderPersistenceFailure : IOrderError;
