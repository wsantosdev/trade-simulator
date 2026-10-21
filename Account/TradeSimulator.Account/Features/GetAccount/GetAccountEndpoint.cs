using TradeSimulator.Account.Infrastructure.Http;

namespace TradeSimulator.Account.Features.GetAccount;

public static class GetAccountEndpoint
{
    public static IEndpointRouteBuilder MapGetAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/accounts/{id:guid}", async (
            Guid id,
            IGetAccount getAccount,
            CancellationToken cancellationToken) =>
        {
            var result = await getAccount.ExecuteAsync(id, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}
