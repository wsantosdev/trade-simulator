using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.GetAccount;

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
