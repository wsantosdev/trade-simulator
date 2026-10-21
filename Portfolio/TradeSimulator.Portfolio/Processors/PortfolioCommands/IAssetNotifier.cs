using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public interface IAssetNotifier
{
    Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken);
}
