using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Infrastructure.Http;

namespace TradeSimulator.Portfolio.Features.DepositAsset;

public static class DepositAssetEndpoint
{
    public static IEndpointRouteBuilder MapDepositAssetEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/assets/deposits", async (
            DepositAssetRequest request,
            IDepositAsset deposit,
            CancellationToken cancellationToken) =>
        {
            var result = await deposit.ExecuteAsync(request.AccountId, request.Ticker, request.Quantity, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AssetReadModel.From(result.ResultValue).ToItem());
        });

        return endpoints;
    }
}

public sealed record DepositAssetRequest(Guid AccountId, string? Ticker, long Quantity);
