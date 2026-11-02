using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Features.Shared;

namespace TradeSimulator.Portfolio.Processors.SettleReservation;

public enum SettlementResult : byte
{
    Settled,
    ReservationNotFound,
    Invalid
}

// Inbound port.
public interface ISettleReservation
{
    Task<SettlementResult> ExecuteAsync(Guid reservationId, Guid operationId, long quantity, bool close, CancellationToken cancellationToken);
}

public sealed class SettleReservationHandler(
    IAssetStore store,
    IAssetNotifier notifier,
    IPortfolioMetrics metrics,
    TimeProvider clock,
    ILogger<SettleReservationHandler> logger) : ISettleReservation
{
    // Withdraws the settled quantity from the reserved one; closing releases what is left. Idempotent by operation id.
    public async Task<SettlementResult> ExecuteAsync(
        Guid reservationId, Guid operationId, long quantity, bool close, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || quantity < 0)
            return SettlementResult.Invalid;

        var found = await store.FindByReservationAsync(reservationId, cancellationToken);
        if (found.IsError)
            return SettlementResult.ReservationNotFound;

        var saved = await store.ExecuteAsync(
            found.ResultValue,
            asset => asset!.Settle(reservationId, operationId, quantity, close, clock.GetUtcNow()),
            cancellationToken);
        if (saved.IsError)
            return SettlementResult.ReservationNotFound;

        metrics.ReservationSettled(close ? "closure" : "partial");
        await notifier.TryNotifyAsync(saved.ResultValue, logger, cancellationToken);
        return SettlementResult.Settled;
    }
}
