using Moonad;

namespace TradingSimulator.Portfolio.Domain;

public readonly record struct AssetId
{
    public Guid Guid { get; }

    private AssetId(Guid guid) => Guid = guid;

    public static AssetId New() => new(Guid.CreateVersion7());

    public static Result<AssetId, IPortfolioError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new AssetId(value)
            : new InvalidAssetId();

    public override string ToString() => Guid.ToString();
}
