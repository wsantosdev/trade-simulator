namespace TradeSimulator.Account.Processors.OrderFunds;

public enum FundsReservationErrorKind : byte
{
    InsufficientFunds,
    AccountNotFound,
    PriceUnavailable,
    InvalidOrder
}
