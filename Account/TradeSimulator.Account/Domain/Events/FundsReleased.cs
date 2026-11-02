using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Events;

public sealed record FundsReleased(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, ReservationId? ReservationId = null)
    : AccountEvent(AccountId, OccurredAt);
