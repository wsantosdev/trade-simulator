using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Application;

public interface IAssetNotifier
{
    Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken);
}
