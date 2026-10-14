using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.ReserveFunds;

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
