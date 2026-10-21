using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Events;

public sealed record MoneyDeposited(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
