namespace TradingSimulator.Portfolio.Application;

public static class PortfolioEventTypes
{
    public const string Deposit = "Deposit";
    public const string Withdraw = "Withdraw";
    public const string Block = "Block";
    public const string Unblock = "Unblock";
    public const string OrderSubmitted = "OrderSubmitted";
    public const string OrderPartiallyFilled = "OrderPartiallyFilled";
    public const string OrderFilled = "OrderFilled";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderRejected = "OrderRejected";
    public const string CommandResult = "PortfolioCommandResult";
}

public sealed record DepositMessage(Guid AccountId, string? Ticker, long Quantity);
public sealed record WithdrawMessage(Guid AccountId, string? Ticker, long Quantity);
public sealed record BlockMessage(Guid AccountId, string? Ticker, long Quantity);
public sealed record UnblockMessage(Guid AccountId, string? Ticker, long Quantity);
public sealed record OrderSubmittedMessage(Guid OrderId, Guid AccountId, string? Ticker, OrderSide Side, long Quantity);

// Exchange fills carry the quantity executed by that event (not the cumulative one).
public sealed record OrderFillMessage(Guid OrderId, long ExecutedQuantity);
public sealed record OrderClosureMessage(Guid OrderId);

public enum AssetBlockErrorKind
{
    InsufficientQuantity,
    AssetNotFound,
    InvalidOrder
}

public sealed record AssetsBlocked(Guid OrderId, Guid AccountId, string Ticker, long Quantity);
public sealed record AssetBlockError(Guid OrderId, Guid AccountId, string Ticker, AssetBlockErrorKind Kind, string Message);

public static class AssetReservationEvents
{
    public const string Exchange = "trading-simulator.portfolio.reservations";
    public const string ResultEventType = "AssetReservationResult";
    public const string BlockedRoutingKey = "assets.blocked";
    public const string FailedRoutingKey = "assets.block.failed";
}

// Per-order custody of a sell order: Blocked is what was reserved, Remaining what is still blocked for it.
public sealed record OrderReservation(Guid OrderId, Guid AccountId, string Ticker, long Blocked, long Remaining, bool Closed);
