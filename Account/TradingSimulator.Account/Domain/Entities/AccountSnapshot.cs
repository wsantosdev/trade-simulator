using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Domain.Entities;

public sealed record AccountSnapshot(
    AccountId AccountId,
    decimal AvailableBalance,
    decimal ProvisionedBalance);
