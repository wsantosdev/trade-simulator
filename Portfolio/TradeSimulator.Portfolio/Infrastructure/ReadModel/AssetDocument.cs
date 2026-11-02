using TradeSimulator.Portfolio.Common;

namespace TradeSimulator.Portfolio.Infrastructure.ReadModel;

// Asset as stored in the read model (MongoDB collection "assets"); field names follow the sort keys (camelCase).
public sealed class AssetDocument
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Ticker { get; set; } = "";
    public long Available { get; set; }
    public long Blocked { get; set; }
    public long Total { get; set; }
    public int Version { get; set; }

    public static AssetDocument From(AssetReadModel model, int version) => new()
    {
        Id = model.AssetId, AccountId = model.AccountId, Ticker = model.Ticker,
        Available = model.Available, Blocked = model.Blocked, Total = model.Total, Version = version
    };

    public AssetItem ToItem() => new(Ticker, Available, Blocked, Total);
}
