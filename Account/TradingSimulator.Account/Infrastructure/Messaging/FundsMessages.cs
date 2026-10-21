using TradingSimulator.Account.Domain.Enums;
using TradingSimulator.Account.Processors.OrderFunds;

namespace TradingSimulator.Account.Infrastructure.Messaging;

public static class FundsEventTypes
{
    public const string OrderSubmitted = "OrderSubmitted";
    public const string OrderPartiallyFilled = "OrderPartiallyFilled";
    public const string OrderFilled = "OrderFilled";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderScheduleCancelled = "OrderScheduleCancelled";
    public const string OrderRejected = "OrderRejected";
    public const string ReservationResult = "FundsReservationResult";
    public const string ReservedRoutingKey = "funds.reserved";
    public const string FailedRoutingKey = "funds.reservation.failed";
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
