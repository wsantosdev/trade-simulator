using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Processors.OrderFunds;

public enum FundsGateResult : byte
{
    Reserved,
    Insufficient,
    // The balance is not cached yet: hydrate it from the event store and try again.
    Miss
}

// Fast, single-threaded admission gate in front of the event store. It is only a cache of the available balance
// plus a ledger of in-flight reservations: PostgreSQL stays the source of truth and rejects anything the gate let through by mistake.
public interface IFundsGate
{
    Task<FundsGateResult> TryReserveAsync(AccountId accountId, OrderId orderId, long cents, CancellationToken cancellationToken);
    Task HydrateAsync(AccountId accountId, long availableCents, CancellationToken cancellationToken);
    // Gives back what the order took from the gate (no-op when it took nothing) and drops the cached balance.
    Task CompensateAsync(AccountId accountId, OrderId orderId, CancellationToken cancellationToken);
    // Drops the cached balance after any change made outside the gate.
    Task InvalidateAsync(AccountId accountId, CancellationToken cancellationToken);
}
