using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Events;

public sealed record FundsDebited(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, OrderId? OrderId = null)
    : AccountEvent(AccountId, OccurredAt);
