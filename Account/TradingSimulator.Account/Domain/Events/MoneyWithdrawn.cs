using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Domain.Events;

public sealed record MoneyWithdrawn(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
