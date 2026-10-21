using TradeSimulator.Account.Infrastructure.Http;

namespace TradeSimulator.Account.Features.WithdrawFunds;

public static class WithdrawFundsEndpoint
{
    public static IEndpointRouteBuilder MapWithdrawEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{id:guid}/withdrawals", async (
            Guid id,
            WithdrawalRequest request,
            IWithdrawFunds withdrawFunds,
            CancellationToken cancellationToken) =>
        {
            var result = await withdrawFunds.ExecuteAsync(id, request.Amount, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}

public sealed record WithdrawalRequest(decimal Amount);
