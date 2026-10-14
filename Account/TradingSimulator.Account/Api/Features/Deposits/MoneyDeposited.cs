using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Domain.Features.Deposits;

public sealed record MoneyDeposited(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
