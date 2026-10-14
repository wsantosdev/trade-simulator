using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.OpenAccount;

public static class OpenAccountEndpoint
{
    public static IEndpointRouteBuilder MapOpenAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts", async (IOpenAccount openAccount, CancellationToken cancellationToken) =>
        {
            var result = await openAccount.ExecuteAsync(cancellationToken);
            if (result.IsError)
                return result.ErrorValue.ToHttpResult();

            var account = result.ResultValue;
            return Results.Created($"/api/accounts/{account.Id.Guid:D}", AccountResponse.From(account));
        });

        return endpoints;
    }
}
