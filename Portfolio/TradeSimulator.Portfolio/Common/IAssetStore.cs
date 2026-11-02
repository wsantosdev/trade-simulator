using Moonad;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Common;

// Outbound port: saves and loads assets. It only stores events; the rules live in the Asset entity.
public interface IAssetStore
{
    // Loads the asset (null when it does not exist yet), runs the operation and saves the new events,
    // one operation at a time per asset.
    Task<Result<Asset, IPortfolioError>> ExecuteAsync(
        AssetId id, Func<Asset?, Result<Asset, IPortfolioError>> operation, CancellationToken cancellationToken);

    Task<Result<Asset, IPortfolioError>> GetAsync(AssetId id, CancellationToken cancellationToken);

    // Which asset holds the reservation (found in its AssetBlocked event).
    Task<Result<AssetId, IPortfolioError>> FindByReservationAsync(Guid reservationId, CancellationToken cancellationToken);
}
