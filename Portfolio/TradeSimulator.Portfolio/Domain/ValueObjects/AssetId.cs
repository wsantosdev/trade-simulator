using Moonad;

namespace TradeSimulator.Portfolio.Domain.ValueObjects;

public readonly record struct AssetId
{
    public Guid Guid { get; }

    private AssetId(Guid guid) => Guid = guid;

    public static AssetId New() => new(Guid.CreateVersion7());

    // One stream per account and ticker: the id is derived from them, so no lookup table is needed.
    public static AssetId For(AccountId accountId, Ticker ticker)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{accountId}:{ticker}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new(new Guid(bytes, bigEndian: true));
    }

    public static Result<AssetId, IPortfolioError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new AssetId(value)
            : new InvalidAssetId();

    public override string ToString() => Guid.ToString();
}
