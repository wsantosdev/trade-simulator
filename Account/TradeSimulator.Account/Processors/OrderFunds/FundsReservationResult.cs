namespace TradeSimulator.Account.Processors.OrderFunds;

public sealed record FundsReservationResult(bool Reserved, FundsReservationErrorKind? Kind, decimal Amount)
{
    public static FundsReservationResult Success(decimal amount) => new(true, null, amount);
    public static FundsReservationResult Failure(FundsReservationErrorKind kind) => new(false, kind, 0m);
}
