using Moonad;
using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Domain;
using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Features.SearchAssets;

public sealed record AssetSearchCriteria(AccountId AccountId, string SortBy, bool Descending, int Page);

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

// Inbound port.
public interface ISearchAssets
{
    Task<Result<AssetPage, IPortfolioError>> ExecuteAsync(
        Guid accountId, string? sortBy, string? sortDirection, int? page, CancellationToken cancellationToken);
}

// Outbound port.
public interface IAssetSearch
{
    public const int PageSize = 50;

    Task<AssetPage> SearchAsync(AssetSearchCriteria criteria, CancellationToken cancellationToken);
}

public sealed class SearchAssetsHandler(IAssetSearch search) : ISearchAssets
{
    public async Task<Result<AssetPage, IPortfolioError>> ExecuteAsync(
        Guid accountId, string? sortBy, string? sortDirection, int? page, CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(accountId);
        if (account.IsError)
            return Result<AssetPage, IPortfolioError>.Error(account.ErrorValue);

        var column = string.IsNullOrWhiteSpace(sortBy) ? "ticker" : sortBy;
        if (!AssetSortFields.Columns.ContainsKey(column))
            return Result<AssetPage, IPortfolioError>.Error(new InvalidSortField());

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return await search.SearchAsync(
            new AssetSearchCriteria(account.ResultValue, column, descending, page ?? 1), cancellationToken);
    }
}
