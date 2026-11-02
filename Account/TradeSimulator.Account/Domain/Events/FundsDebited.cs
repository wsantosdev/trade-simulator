using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Events;

public sealed record FundsDebited(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, ReservationId? ReservationId = null, Guid? OperationId = null)
    : AccountEvent(AccountId, OccurredAt);
