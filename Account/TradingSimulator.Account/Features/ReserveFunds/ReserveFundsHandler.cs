using Moonad;
using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Features.Shared;

namespace TradingSimulator.Account.Features.ReserveFunds;

// Inbound port.
public interface IReserveFunds
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken);
}

public sealed class ReserveFundsHandler(IAccountStore store, IAccountMetrics metrics) : IReserveFunds
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue);

        var result = await store.ExecuteAsync(id.ResultValue, current => current.Reserve(amount), cancellationToken);
        metrics.OperationCompleted("reserve", amount, result.IsError ? result.ErrorValue : null);
        return result;
    }
}
