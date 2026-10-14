using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Domain.Events;

public sealed record AccountOpened(AccountId AccountId, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
