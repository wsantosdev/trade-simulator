namespace TradingSimulator.Account.Application;

public static class FundsEventTypes
{
    public const string OrderSubmitted = "OrderSubmitted";
    public const string OrderPartiallyFilled = "OrderPartiallyFilled";
    public const string OrderFilled = "OrderFilled";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderRejected = "OrderRejected";
    public const string ReservationResult = "FundsReservationResult";
    public const string ReservedRoutingKey = "funds.reserved";
    public const string FailedRoutingKey = "funds.reservation.failed";
}

public enum OrderSide
{
    Buy = 1,
    Sell = 2
}

public enum OrderType
{
    Market = 1,
    Limit = 2
}

public enum FundsReservationErrorKind
{
    InsufficientFunds,
    AccountNotFound,
    PriceUnavailable,
    InvalidOrder
}

public sealed record FundsReservedMessage(Guid OrderId, Guid AccountId, decimal Amount);

public sealed record FundsReservationError(Guid OrderId, Guid AccountId, FundsReservationErrorKind Kind, string Message);

public sealed record OrderSubmittedMessage(
    Guid OrderId,
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price);

// Exchange fills; the price travels as "price" in the contract and as "executionPrice" in the Exchange messages.
public sealed record OrderFillMessage(Guid OrderId, long ExecutedQuantity, decimal? Price, decimal? ExecutionPrice);

public sealed record OrderClosureMessage(Guid OrderId);
