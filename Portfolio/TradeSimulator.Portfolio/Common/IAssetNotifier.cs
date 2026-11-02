using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Common;

// Outbound port: tells the connected clients that an asset changed.
public interface IAssetNotifier
{
    Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken);
}
