using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.OpenAccount;

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
