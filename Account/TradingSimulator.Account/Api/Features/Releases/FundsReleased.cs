using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Domain.Features.Releases;

public sealed record FundsReleased(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, OrderId? OrderId = null)
    : AccountEvent(AccountId, OccurredAt);
