using Moonad;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public interface IAssetStore
{
    // Finds the active asset (Total > 0) of the account; AssetNotFound when there is none.
    Task<Result<Asset, IPortfolioError>> FindAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken);

    // Persists the first events, the response and the active-asset slot atomically; AssetAlreadyExists on a race.
    Task<Result<Asset, IPortfolioError>> CreateAsync(Asset asset, PortfolioCommandResult result, CancellationToken cancellationToken);

    // Persists the new events and the response atomically; ConcurrencyConflict when the stream moved on.
    Task<Result<Asset, IPortfolioError>> AppendAsync(Asset asset, PortfolioCommandResult result, CancellationToken cancellationToken);

    // Persists the blocked asset events, the order reservation and the success response atomically.
    // DuplicateEnvelope when the envelope or the order was already reserved; ConcurrencyConflict when the stream moved on.
    Task<Result<Asset, IPortfolioError>> ReserveAsync(
        Guid envelopeId, Asset asset, OrderReservation reservation, AssetsBlocked blocked, CancellationToken cancellationToken);

    // Queues the failure response of a reservation; false when the envelope was already answered.
    Task<bool> RejectReservationAsync(Guid envelopeId, AssetBlockError error, CancellationToken cancellationToken);

    Task<OrderReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken);

    // Atomically applies the asset events (asset may be null or without events), moves the reservation from
    // expectedRemaining to newRemaining (closing it when asked) and records the envelope as processed.
    // DuplicateEnvelope when already processed; ConcurrencyConflict when the asset or the reservation moved on.
    Task<Result<bool, IPortfolioError>> SettleAsync(
        Guid envelopeId, Asset? asset, Guid orderId, long expectedRemaining, long newRemaining, bool close, CancellationToken cancellationToken);
}
