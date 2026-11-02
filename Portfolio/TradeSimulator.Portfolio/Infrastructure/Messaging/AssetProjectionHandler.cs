using System.Text.Json;
using TradeSimulator.Commons;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Infrastructure.ReadModel;
using TradeSimulator.Portfolio.Infrastructure.Serialization;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

// Reacts to every asset event published by the outbox and refreshes the MongoDB read model.
// The message only says which asset changed: the view is rebuilt from the asset's stream and written
// only if newer than the stored one, so duplicates and out-of-order deliveries are harmless.
public sealed class AssetProjectionHandler(IAssetStore store, MongoAssetReadModel readModel, ILogger<AssetProjectionHandler> logger)
{
    private readonly SemaphoreSlim _indexes = new(1, 1);
    private bool _indexesCreated;

    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        EventEnvelope<AssetChanged>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<AssetChanged>>(body, PortfolioJson.Options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded a malformed asset event.");
            return;
        }

        if (envelope?.Event is null)
        {
            logger.LogWarning("Discarded an asset event without payload.");
            return;
        }

        await EnsureIndexesAsync(cancellationToken);

        var loaded = await store.GetAsync(AssetId.TryCreate(envelope.Event.AssetId).ResultValue, cancellationToken);
        if (loaded.IsError)
        {
            logger.LogWarning("Asset {AssetId} could not be loaded for projection: {Error}.", envelope.Event.AssetId, loaded.ErrorValue);
            return;
        }

        var asset = loaded.ResultValue;
        if (asset.IsEmpty)
            await readModel.RemoveAsync(envelope.Event.AssetId, asset.Version, cancellationToken);
        else
            await readModel.UpsertAsync(AssetReadModel.From(asset), asset.Version, cancellationToken);
    }

    private async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexesCreated)
            return;
        await _indexes.WaitAsync(cancellationToken);
        try
        {
            if (_indexesCreated)
                return;
            await readModel.EnsureIndexesAsync(cancellationToken);
            _indexesCreated = true;
        }
        finally
        {
            _indexes.Release();
        }
    }
}
