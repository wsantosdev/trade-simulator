using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Features.Shared;

namespace TradeSimulator.Portfolio.Features.ReserveAssets;

public sealed record ReservationResult(bool Reserved, ReservationFailure? Failure);

// Inbound port.
public interface IReserveAssets
{
    Task<ReservationResult> ExecuteAsync(Guid reservationId, Guid accountId, string? ticker, long quantity, CancellationToken cancellationToken);
}

public sealed class ReserveAssetsHandler(
    IAssetStore store,
    IAssetNotifier notifier,
    IPortfolioMetrics metrics,
    TimeProvider clock,
    ILogger<ReserveAssetsHandler> logger) : IReserveAssets
{
    // Blocks the quantity under the reservation id, or fails when it is not available. Idempotent by reservation id.
    public async Task<ReservationResult> ExecuteAsync(
        Guid reservationId, Guid accountId, string? ticker, long quantity, CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(accountId);
        var symbol = Ticker.TryCreate(ticker);
        var amount = Quantity.TryCreate(quantity);
        if (reservationId == Guid.Empty || account.IsError || symbol.IsError || amount.IsError)
            return Rejected(ReservationFailure.Invalid, reservationId);

        var saved = await store.ExecuteAsync(
            AssetId.For(account.ResultValue, symbol.ResultValue),
            asset => asset is null
                ? new AssetNotFound()
                : asset.Reserve(reservationId, amount.ResultValue, clock.GetUtcNow()),
            cancellationToken);
        if (saved.IsError)
        {
            return Rejected(saved.ErrorValue switch
            {
                AssetNotFound => ReservationFailure.AssetNotFound,
                InsufficientAvailableQuantity => ReservationFailure.InsufficientQuantity,
                ReservationConflict => ReservationFailure.Conflict,
                _ => ReservationFailure.Invalid
            }, reservationId);
        }

        metrics.ReservationBlocked();
        await notifier.TryNotifyAsync(saved.ResultValue, logger, cancellationToken);
        return new ReservationResult(true, null);
    }

    private ReservationResult Rejected(ReservationFailure failure, Guid reservationId)
    {
        metrics.ReservationRejected(failure.ToString());
        logger.LogWarning("Reservation {ReservationId} failed ({Failure}).", reservationId, failure);
        return new ReservationResult(false, failure);
    }
}
