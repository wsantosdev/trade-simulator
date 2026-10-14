using Moonad;
using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Features.Shared;

namespace TradingSimulator.Account.Features.WithdrawFunds;

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
