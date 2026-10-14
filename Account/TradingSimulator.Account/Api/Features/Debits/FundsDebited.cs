using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Domain.Features.Debits;

public sealed record FundsDebited(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, OrderId? OrderId = null)
    : AccountEvent(AccountId, OccurredAt);
