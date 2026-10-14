using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Domain.Events;

public sealed record MoneyDeposited(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
