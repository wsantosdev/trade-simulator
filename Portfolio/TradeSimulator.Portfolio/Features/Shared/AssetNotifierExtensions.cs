using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain.Entities;

namespace TradeSimulator.Portfolio.Features.Shared;

public static class AssetNotifierExtensions
{
    // The state is already committed: a notification failure must never fail the request.
    public static async Task TryNotifyAsync(this IAssetNotifier notifier, Asset asset, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await notifier.AssetChangedAsync(asset.AccountId, AssetReadModel.From(asset).ToItem(), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not notify the change of asset {AssetId}.", asset.Id);
        }
    }
}
