using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Events;

public abstract record AccountEvent(AccountId AccountId, DateTimeOffset OccurredAt);
