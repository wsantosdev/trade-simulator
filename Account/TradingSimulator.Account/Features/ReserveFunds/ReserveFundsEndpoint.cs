using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.ReserveFunds;

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
            var result = await reserveFunds.ExecuteAsync(id, request.Amount, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}

public sealed record ReserveFundsRequest(decimal Amount);
