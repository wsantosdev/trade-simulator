using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Events;

public sealed record AccountOpened(AccountId AccountId, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
