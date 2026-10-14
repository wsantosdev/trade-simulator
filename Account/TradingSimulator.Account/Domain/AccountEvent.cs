namespace TradingSimulator.Account.Domain;

public abstract record AccountEvent(AccountId AccountId, DateTimeOffset OccurredAt);
