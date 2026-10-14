using TradingSimulator.Portfolio.Infrastructure.Http;

namespace TradingSimulator.Portfolio.Features.GetAsset;

public static class GetAssetEndpoint
{
    public static IEndpointRouteBuilder MapGetAssetEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/assets/{ticker}", async (
            string ticker,
            Guid accountId,
            IGetAsset getAsset,
            CancellationToken cancellationToken) =>
        {
            var result = await getAsset.ExecuteAsync(accountId, ticker, cancellationToken);
            return result.IsError ? result.ErrorValue.ToHttpResult() : Results.Ok(result.ResultValue);
        });

        return endpoints;
    }
}
