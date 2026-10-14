using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Common;

public interface IAssetNotifier
{
    Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken);
}
