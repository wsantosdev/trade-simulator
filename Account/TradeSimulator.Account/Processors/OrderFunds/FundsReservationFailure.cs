namespace TradeSimulator.Account.Processors.OrderFunds;

public sealed record FundsReservationFailure(Guid OrderId, Guid AccountId, FundsReservationErrorKind Kind, string Reason);
