using TradingSimulator.Portfolio.Infrastructure.Http;

namespace TradingSimulator.Portfolio.Features.SearchAssets;

public sealed record SearchAssetsRequest(Guid AccountId, string? SortBy, string? SortDirection, int? Page);

public static class SearchAssetsEndpoint
{
    public static IEndpointRouteBuilder MapSearchAssetsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/assets", async (
            [AsParameters] SearchAssetsRequest request,
            ISearchAssets searchAssets,
            CancellationToken cancellationToken) =>
        {
            var result = await searchAssets.ExecuteAsync(
                request.AccountId, request.SortBy, request.SortDirection, request.Page, cancellationToken);
            return result.IsError ? result.ErrorValue.ToHttpResult() : Results.Ok(result.ResultValue);
        });

        return endpoints;
    }
}
