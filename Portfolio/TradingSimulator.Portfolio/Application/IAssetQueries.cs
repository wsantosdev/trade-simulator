using Moonad;
using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Application;

public sealed record AssetSearch(AccountId AccountId, string SortBy, bool Descending, int Page);

public sealed record AssetPage(IReadOnlyList<AssetItem> Items, int Page, int PageSize, long TotalCount);

public static class AssetSortFields
{
    public static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ticker"] = "ticker",
        ["total"] = "total",
        ["available"] = "available",
        ["blocked"] = "blocked"
    };
}

public interface IAssetQueries
{
    public const int PageSize = 50;

    Task<AssetPage> SearchAsync(AssetSearch search, CancellationToken cancellationToken);
    Task<Result<AssetItem, IPortfolioError>> GetAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken);
}
