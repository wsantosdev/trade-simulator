using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Shared;

namespace TradeSimulator.Account.Features.Deposit;

// Inbound port.
public interface IDeposit
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken);
}

public sealed class DepositHandler(IAccountStore store, IAccountMetrics metrics) : IDeposit
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue);

        var result = await store.ExecuteAsync(id.ResultValue, current => current.Deposit(amount), cancellationToken);
        metrics.OperationCompleted("deposit", amount, result.IsError ? result.ErrorValue : null);
        return result;
    }
}
