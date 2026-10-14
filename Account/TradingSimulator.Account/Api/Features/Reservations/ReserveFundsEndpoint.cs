using TradingSimulator.Account.Api;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api.Features.Reservations;

public static class ReserveFundsEndpoint
{
    public static IEndpointRouteBuilder MapReserveFundsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{id:guid}/reservations", async (
            Guid id,
            ReserveFundsRequest request,
            IAccountStore store,
            CancellationToken cancellationToken) =>
        {
            var accountId = AccountId.TryCreate(id);
            if (accountId.IsError)
                return accountId.ErrorValue.ToHttpResult();

            var result = await store.ExecuteAsync(accountId.ResultValue, current => current.Reserve(request.Amount), cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}

public sealed record ReserveFundsRequest(decimal Amount);
