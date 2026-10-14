using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.Deposit;

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
