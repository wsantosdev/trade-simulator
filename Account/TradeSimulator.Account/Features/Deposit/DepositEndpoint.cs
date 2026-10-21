using TradeSimulator.Account.Infrastructure.Http;

namespace TradeSimulator.Account.Features.Deposit;

public static class DepositEndpoint
{
    public static IEndpointRouteBuilder MapDepositEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{id:guid}/deposits", async (
            Guid id,
            DepositRequest request,
            IDeposit deposit,
            CancellationToken cancellationToken) =>
        {
            var result = await deposit.ExecuteAsync(id, request.Amount, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}

public sealed record DepositRequest(decimal Amount);
