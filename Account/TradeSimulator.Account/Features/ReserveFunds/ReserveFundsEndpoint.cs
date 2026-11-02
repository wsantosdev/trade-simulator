using TradeSimulator.Account.Infrastructure.Http;

namespace TradeSimulator.Account.Features.ReserveFunds;

public static class ReserveFundsEndpoint
{
    public static IEndpointRouteBuilder MapReserveFundsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{id:guid}/reservations", async (
            Guid id,
            ReserveFundsRequest request,
            IReserveFunds reserveFunds,
            CancellationToken cancellationToken) =>
        {
            var result = await reserveFunds.ExecuteAsync(request.ReservationId, id, request.Amount, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(new ReserveFundsResponse(request.ReservationId, id, request.Amount));
        });

        return endpoints;
    }
}

public sealed record ReserveFundsRequest(Guid ReservationId, decimal Amount);

public sealed record ReserveFundsResponse(Guid ReservationId, Guid AccountId, decimal Amount);
