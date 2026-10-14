using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Domain.Features.Withdrawals;

public sealed record MoneyWithdrawn(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
