using Moonad;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.Events;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Tests;

// Keeps the saved events in memory and rebuilds assets from them, like the real store does.
internal sealed class FakeStore : IAssetStore
{
    private readonly Dictionary<AssetId, List<AssetEvent>> _streams = [];

    public IEnumerable<Asset> Assets => _streams.Values.Select(Rebuild);

    public async Task<Result<Asset, IPortfolioError>> ExecuteAsync(
        AssetId id, Func<Asset?, Result<Asset, IPortfolioError>> operation, CancellationToken cancellationToken)
    {
        var result = operation(_streams.TryGetValue(id, out var history) ? Rebuild(history) : null);
        if (result.IsError)
            return result;

        var asset = result.ResultValue;
        if (asset.UncommittedEvents.Count > 0)
            _streams[id] = [.. _streams.GetValueOrDefault(id) ?? [], .. asset.UncommittedEvents.Cast<AssetEvent>()];
        asset.CommitEvents();
        return await Task.FromResult(result);
    }

    public Task<Result<Asset, IPortfolioError>> GetAsync(AssetId id, CancellationToken cancellationToken) =>
        Task.FromResult<Result<Asset, IPortfolioError>>(_streams.TryGetValue(id, out var history) ? Rebuild(history) : new AssetNotFound());

    public Task<Result<AssetId, IPortfolioError>> FindByReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var stream = _streams.FirstOrDefault(s => s.Value.OfType<AssetBlocked>().Any(b => b.ReservationId == reservationId));
        return Task.FromResult<Result<AssetId, IPortfolioError>>(stream.Value is null ? new ReservationNotFound() : stream.Key);
    }

    private static Asset Rebuild(List<AssetEvent> history) => Asset.Rehydrate(history).ResultValue;
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
