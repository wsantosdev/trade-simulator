using TradeSimulator.Portfolio.Features.Shared;

namespace TradeSimulator.Portfolio.Features.ReserveAssets;

public static class ReserveAssetsEndpoint
{
    public static IEndpointRouteBuilder MapReserveAssetsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/assets/reservations", async (
            ReserveAssetsRequest request,
            IReserveAssets reserve,
            CancellationToken cancellationToken) =>
        {
            var result = await reserve.ExecuteAsync(
                request.ReservationId, request.AccountId, request.Ticker, request.Quantity, cancellationToken);
            return result.Failure is { } failure
                ? failure.ToHttpResult()
                : Results.Ok(new ReserveAssetsResponse(request.ReservationId, request.AccountId, request.Ticker, request.Quantity));
        });

        return endpoints;
    }
}

public sealed record ReserveAssetsRequest(Guid ReservationId, Guid AccountId, string? Ticker, long Quantity);

public sealed record ReserveAssetsResponse(Guid ReservationId, Guid AccountId, string? Ticker, long Quantity);
