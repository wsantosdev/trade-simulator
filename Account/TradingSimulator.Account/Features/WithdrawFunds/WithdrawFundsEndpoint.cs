using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Http;

namespace TradingSimulator.Account.Features.WithdrawFunds;

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
