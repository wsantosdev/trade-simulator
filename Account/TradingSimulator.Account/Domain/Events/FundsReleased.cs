using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Domain.Events;

public sealed record FundsReleased(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, OrderId? OrderId = null)
    : AccountEvent(AccountId, OccurredAt);
