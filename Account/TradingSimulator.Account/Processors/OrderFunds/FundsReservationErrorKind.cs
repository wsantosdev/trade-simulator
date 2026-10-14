namespace TradingSimulator.Account.Processors.OrderFunds;

public enum FundsReservationErrorKind : byte
{
    InsufficientFunds,
    AccountNotFound,
    PriceUnavailable,
    InvalidOrder
}
