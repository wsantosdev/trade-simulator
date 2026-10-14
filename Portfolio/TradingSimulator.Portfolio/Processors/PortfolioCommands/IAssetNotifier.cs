using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

public interface IAssetNotifier
{
    Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken);
}
