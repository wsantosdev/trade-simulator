using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Shared;

namespace TradeSimulator.Account.Features.WithdrawFunds;

// Inbound port.
public interface IWithdrawFunds
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken);
}

public sealed class WithdrawFundsHandler(IAccountStore store, IAccountMetrics metrics) : IWithdrawFunds
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue);

        var result = await store.ExecuteAsync(id.ResultValue, current => current.Withdraw(amount), cancellationToken);
        metrics.OperationCompleted("withdraw", amount, result.IsError ? result.ErrorValue : null);
        return result;
    }
}
