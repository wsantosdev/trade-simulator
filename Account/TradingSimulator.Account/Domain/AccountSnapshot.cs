namespace TradingSimulator.Account.Domain;

public sealed record AccountSnapshot(
    AccountId AccountId,
    decimal AvailableBalance,
    decimal ProvisionedBalance);
