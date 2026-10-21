using Moonad;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Features.GetAsset;

// Inbound port.
public interface IGetAsset
{
    Task<Result<AssetItem, IPortfolioError>> ExecuteAsync(Guid accountId, string ticker, CancellationToken cancellationToken);
}

// Outbound port.
public interface IAssetLookup
{
    Task<Result<AssetItem, IPortfolioError>> GetAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken);
}

public sealed class GetAssetHandler(IAssetLookup lookup) : IGetAsset
{
    public async Task<Result<AssetItem, IPortfolioError>> ExecuteAsync(Guid accountId, string ticker, CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(accountId);
        if (account.IsError)
            return Result<AssetItem, IPortfolioError>.Error(account.ErrorValue);
        var parsedTicker = Ticker.TryCreate(ticker);
        if (parsedTicker.IsError)
            return Result<AssetItem, IPortfolioError>.Error(parsedTicker.ErrorValue);

        return await lookup.GetAsync(account.ResultValue, parsedTicker.ResultValue, cancellationToken);
    }
}
