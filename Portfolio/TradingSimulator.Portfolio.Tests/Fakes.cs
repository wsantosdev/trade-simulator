using Moonad;
using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Domain;
using TradingSimulator.Portfolio.Domain.Entities;
using TradingSimulator.Portfolio.Domain.ValueObjects;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradingSimulator.Portfolio.Tests;

internal sealed class FakeStore : IAssetStore
{
    public Dictionary<AssetId, Asset> Assets { get; } = [];
    public List<PortfolioCommandResult> Responses { get; } = [];
    public Exception? FailWith { get; set; }
    public int ConflictsLeft { get; set; }
    public bool DuplicateEnvelope { get; set; }
    public Dictionary<Guid, OrderReservation> Reservations { get; } = [];
    public List<Result<AssetsBlocked, AssetBlockError>> ReservationResponses { get; } = [];
    public HashSet<Guid> SeenEnvelopes { get; } = [];

    public Task<Result<Asset, IPortfolioError>> ReserveAsync(
        Guid envelopeId, Asset asset, OrderReservation reservation, AssetsBlocked blocked, CancellationToken cancellationToken)
    {
        if (!SeenEnvelopes.Add(envelopeId) || Reservations.ContainsKey(reservation.OrderId))
            return Task.FromResult<Result<Asset, IPortfolioError>>(new DuplicateEnvelope());

        Assets[asset.Id] = Asset.Restore(asset.ToSnapshot(), []).ResultValue;
        Reservations[reservation.OrderId] = reservation;
        ReservationResponses.Add(blocked);
        asset.MarkCommitted();
        return Task.FromResult<Result<Asset, IPortfolioError>>(asset);
    }

    public Task<bool> RejectReservationAsync(Guid envelopeId, AssetBlockError error, CancellationToken cancellationToken)
    {
        if (!SeenEnvelopes.Add(envelopeId))
            return Task.FromResult(false);
        ReservationResponses.Add(error);
        return Task.FromResult(true);
    }

    public Task<OrderReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Reservations.GetValueOrDefault(orderId));

    public Task<Result<bool, IPortfolioError>> SettleAsync(
        Guid envelopeId, Asset? asset, Guid orderId, long expectedRemaining, long newRemaining, bool close, CancellationToken cancellationToken)
    {
        if (!SeenEnvelopes.Add(envelopeId))
            return Task.FromResult<Result<bool, IPortfolioError>>(new DuplicateEnvelope());

        if (asset is not null)
        {
            Assets[asset.Id] = Asset.Restore(asset.ToSnapshot(), []).ResultValue;
            asset.MarkCommitted();
        }

        Reservations[orderId] = Reservations[orderId] with { Remaining = newRemaining, Closed = close };
        return Task.FromResult<Result<bool, IPortfolioError>>(true);
    }

    public Task<Result<Asset, IPortfolioError>> FindAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
            throw FailWith;

        var active = Assets.Values.FirstOrDefault(a => a.AccountId == accountId && a.Ticker == ticker && !a.IsRemoved);
        return Task.FromResult<Result<Asset, IPortfolioError>>(active is null ? new AssetNotFound() : Asset.Restore(active.ToSnapshot(), []).ResultValue);
    }

    public Task<Result<Asset, IPortfolioError>> CreateAsync(Asset asset, PortfolioCommandResult result, CancellationToken cancellationToken) =>
        Save(asset, result);

    public Task<Result<Asset, IPortfolioError>> AppendAsync(Asset asset, PortfolioCommandResult result, CancellationToken cancellationToken) =>
        Save(asset, result);

    private Task<Result<Asset, IPortfolioError>> Save(Asset asset, PortfolioCommandResult result)
    {
        if (DuplicateEnvelope)
            return Task.FromResult<Result<Asset, IPortfolioError>>(new DuplicateEnvelope());

        if (ConflictsLeft > 0)
        {
            ConflictsLeft--;
            return Task.FromResult<Result<Asset, IPortfolioError>>(new ConcurrencyConflict());
        }

        Assets[asset.Id] = Asset.Restore(asset.ToSnapshot(), []).ResultValue;
        Responses.Add(result);
        return Task.FromResult<Result<Asset, IPortfolioError>>(asset);
    }
}

internal sealed class FakeIdempotency : IIdempotencyStore
{
    public HashSet<Guid> Processed { get; } = [];
    public HashSet<Guid> Locked { get; } = [];
    public int Disposed { get; set; }

    public Task<IEnvelopeLease?> TryLockAsync(Guid envelopeId, CancellationToken cancellationToken) =>
        Task.FromResult<IEnvelopeLease?>(Locked.Add(envelopeId) ? new Lease(this, envelopeId) : null);

    internal sealed class Lease(FakeIdempotency owner, Guid id) : IEnvelopeLease
    {
        public Task<bool> WasProcessedAsync(CancellationToken cancellationToken) => Task.FromResult(owner.Processed.Contains(id));

        public Task MarkProcessedAsync(CancellationToken cancellationToken)
        {
            owner.Processed.Add(id);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owner.Locked.Remove(id);
            owner.Disposed++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeResponses : IResponsePublisher
{
    public List<PortfolioCommandError> Failures { get; } = [];

    public Task PublishFailureAsync(PortfolioCommandError failure, CancellationToken cancellationToken)
    {
        Failures.Add(failure);
        return Task.CompletedTask;
    }

    public Task PublishPendingAsync(PendingResponse pending, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeNotifier : IAssetNotifier
{
    public List<AssetItem> Notified { get; } = [];
    public bool Fail { get; set; }

    public Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken)
    {
        if (Fail)
            throw new InvalidOperationException("hub down");
        Notified.Add(asset);
        return Task.CompletedTask;
    }
}
