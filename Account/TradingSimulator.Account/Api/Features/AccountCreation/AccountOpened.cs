using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Domain.Features.AccountCreation;

public sealed record AccountOpened(AccountId AccountId, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
