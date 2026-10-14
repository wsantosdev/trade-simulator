using TradingSimulator.Portfolio.Application;
using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Api.Features.Queries;

public sealed record SearchAssetsRequest(Guid AccountId, string? SortBy, string? SortDirection, int? Page);

public static class AssetQueryEndpoints
{
    public static IEndpointRouteBuilder MapAssetQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/assets", async (
            [AsParameters] SearchAssetsRequest request,
            IAssetQueries queries,
            CancellationToken cancellationToken) =>
        {
            var account = AccountId.TryCreate(request.AccountId);
            if (account.IsError)
                return account.ErrorValue.ToHttpResult();

            var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "ticker" : request.SortBy;
            if (!AssetSortFields.Columns.ContainsKey(sortBy))
                return new InvalidSortField().ToHttpResult();

            var descending = string.Equals(request.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            var page = await queries.SearchAsync(
                new AssetSearch(account.ResultValue, sortBy, descending, request.Page ?? 1), cancellationToken);
            return Results.Ok(page);
        });

        endpoints.MapGet("/api/assets/{ticker}", async (
            string ticker,
            Guid accountId,
            IAssetQueries queries,
            CancellationToken cancellationToken) =>
        {
            var account = AccountId.TryCreate(accountId);
            if (account.IsError)
                return account.ErrorValue.ToHttpResult();
            var parsedTicker = Ticker.TryCreate(ticker);
            if (parsedTicker.IsError)
                return parsedTicker.ErrorValue.ToHttpResult();

            var asset = await queries.GetAsync(account.ResultValue, parsedTicker.ResultValue, cancellationToken);
            return asset.IsError ? asset.ErrorValue.ToHttpResult() : Results.Ok(asset.ResultValue);
        });

        return endpoints;
    }
}
