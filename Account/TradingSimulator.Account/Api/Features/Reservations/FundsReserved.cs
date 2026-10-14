using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Domain.Features.Reservations;

public sealed record FundsReserved(AccountId AccountId, decimal Amount, DateTimeOffset OccurredAt, OrderId? OrderId = null)
    : AccountEvent(AccountId, OccurredAt);
