using TradingSimulator.Account.Api;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api.Features.AccountQuery;

public static class GetAccountEndpoint
{
    public static IEndpointRouteBuilder MapGetAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/accounts/{id:guid}", async (
            Guid id,
            IAccountStore store,
            CancellationToken cancellationToken) =>
        {
            var accountId = AccountId.TryCreate(id);
            if (accountId.IsError)
                return accountId.ErrorValue.ToHttpResult();

            var result = await store.GetAsync(accountId.ResultValue, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}
