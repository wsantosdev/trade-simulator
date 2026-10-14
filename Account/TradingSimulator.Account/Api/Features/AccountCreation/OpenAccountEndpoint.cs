using TradingSimulator.Account.Api;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api.Features.AccountCreation;

public static class OpenAccountEndpoint
{
    public static IEndpointRouteBuilder MapOpenAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts", async (IAccountStore store, CancellationToken cancellationToken) =>
        {
            var result = await store.OpenAsync(AccountId.New(), cancellationToken);
            if (result.IsError)
                return result.ErrorValue.ToHttpResult();

            var account = result.ResultValue;
            return Results.Created($"/api/accounts/{account.Id.Guid:D}", AccountResponse.From(account));
        });

        return endpoints;
    }
}
