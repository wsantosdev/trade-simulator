using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Domain.Entities;

public sealed record AccountSnapshot(
    AccountId AccountId,
    decimal AvailableBalance,
    decimal ProvisionedBalance);
