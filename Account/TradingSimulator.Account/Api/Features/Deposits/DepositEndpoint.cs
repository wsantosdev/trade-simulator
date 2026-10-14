using TradingSimulator.Account.Api;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api.Features.Deposits;

public static class DepositEndpoint
{
    public static IEndpointRouteBuilder MapDepositEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{id:guid}/deposits", async (
            Guid id,
            DepositRequest request,
            IAccountStore store,
            CancellationToken cancellationToken) =>
        {
            var accountId = AccountId.TryCreate(id);
            if (accountId.IsError)
                return accountId.ErrorValue.ToHttpResult();

            var result = await store.ExecuteAsync(accountId.ResultValue, current => current.Deposit(request.Amount), cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}

public sealed record DepositRequest(decimal Amount);
