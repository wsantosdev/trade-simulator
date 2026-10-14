using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Application;

// Query model as stored in the database (asset_read_model).
public sealed record AssetReadModel(Guid AssetId, Guid AccountId, string Ticker, long Available, long Blocked, long Total)
{
    public static AssetReadModel From(Asset asset) =>
        new(asset.Id.Guid, asset.AccountId.Guid, asset.Ticker.Value, asset.Available.Value, asset.Blocked.Value, asset.Total.Value);

    public AssetItem ToItem() => new(Ticker, Available, Blocked, Total);
}
