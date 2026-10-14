using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Domain.Events;

public abstract record AccountEvent(AccountId AccountId, DateTimeOffset OccurredAt);
