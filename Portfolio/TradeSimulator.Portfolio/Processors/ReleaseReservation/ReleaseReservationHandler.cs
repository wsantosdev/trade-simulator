using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Features.Shared;

namespace TradeSimulator.Portfolio.Processors.ReleaseReservation;

// Inbound port.
public interface IReleaseReservation
{
    // Idempotent: an unknown or already closed reservation changes nothing.
    Task ExecuteAsync(Guid reservationId, CancellationToken cancellationToken);
}

public sealed class ReleaseReservationHandler(
    IAssetStore store,
    IAssetNotifier notifier,
    IPortfolioMetrics metrics,
    TimeProvider clock,
    ILogger<ReleaseReservationHandler> logger) : IReleaseReservation
{
    public async Task ExecuteAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var found = await store.FindByReservationAsync(reservationId, cancellationToken);
        if (found.IsError)
            return;

        var saved = await store.ExecuteAsync(
            found.ResultValue,
            asset => asset!.CloseReservation(reservationId, clock.GetUtcNow()),
            cancellationToken);
        if (saved.IsError)
            return;

        metrics.ReservationSettled("closure");
        await notifier.TryNotifyAsync(saved.ResultValue, logger, cancellationToken);
    }
}
