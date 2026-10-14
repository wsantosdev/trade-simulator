using Microsoft.AspNetCore.SignalR;
using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;
using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Infrastructure.Realtime;

public sealed class SignalRAssetNotifier(IHubContext<PortfolioHub> hub, ILogger<SignalRAssetNotifier> logger) : IAssetNotifier
{
    // Best effort; a Total of zero tells the UI to remove the asset.
    public async Task AssetChangedAsync(AccountId accountId, AssetItem asset, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(PortfolioHub.GroupFor(accountId.Guid)).SendAsync("AssetChanged", asset, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not push AssetChanged to account {AccountId}.", accountId.Guid);
        }
    }
}
