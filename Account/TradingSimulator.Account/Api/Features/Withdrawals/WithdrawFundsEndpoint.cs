using TradingSimulator.Account.Api;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Api.Features.Withdrawals;

public static class WithdrawFundsEndpoint
{
    public static IEndpointRouteBuilder MapWithdrawEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/accounts/{id:guid}/withdrawals", async (
            Guid id,
            WithdrawalRequest request,
            IAccountStore store,
            CancellationToken cancellationToken) =>
        {
            var accountId = AccountId.TryCreate(id);
            if (accountId.IsError)
                return accountId.ErrorValue.ToHttpResult();

            var result = await store.ExecuteAsync(accountId.ResultValue, current => current.Withdraw(request.Amount), cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Ok(AccountResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}

public sealed record WithdrawalRequest(decimal Amount);
