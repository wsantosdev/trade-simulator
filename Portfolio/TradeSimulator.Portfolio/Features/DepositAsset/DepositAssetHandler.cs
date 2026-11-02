using Moonad;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Features.Shared;

namespace TradeSimulator.Portfolio.Features.DepositAsset;

// Inbound port.
public interface IDepositAsset
{
    Task<Result<Asset, IPortfolioError>> ExecuteAsync(Guid accountId, string? ticker, long quantity, CancellationToken cancellationToken);
}

public sealed class DepositAssetHandler(
    IAssetStore store,
    IAssetNotifier notifier,
    IPortfolioMetrics metrics,
    TimeProvider clock,
    ILogger<DepositAssetHandler> logger) : IDepositAsset
{
    // The first deposit opens the asset; the next ones add to it.
    public async Task<Result<Asset, IPortfolioError>> ExecuteAsync(
        Guid accountId, string? ticker, long quantity, CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(accountId);
        if (account.IsError)
            return Rejected(account.ErrorValue);
        var symbol = Ticker.TryCreate(ticker);
        if (symbol.IsError)
            return Rejected(symbol.ErrorValue);
        var amount = Quantity.TryCreate(quantity);
        if (amount.IsError)
            return Rejected(amount.ErrorValue);

        var id = AssetId.For(account.ResultValue, symbol.ResultValue);
        var now = clock.GetUtcNow();
        var saved = await store.ExecuteAsync(
            id,
            asset => asset is null
                ? Asset.Open(id, account.ResultValue, symbol.ResultValue, amount.ResultValue, now)
                : asset.Deposit(amount.ResultValue, now),
            cancellationToken);
        if (saved.IsError)
            return Rejected(saved.ErrorValue);

        metrics.CommandApplied("deposit");
        await notifier.TryNotifyAsync(saved.ResultValue, logger, cancellationToken);
        return saved;
    }

    private Result<Asset, IPortfolioError> Rejected(IPortfolioError error)
    {
        metrics.CommandRejected(error.GetType().Name);
        return Result<Asset, IPortfolioError>.Error(error);
    }
}
