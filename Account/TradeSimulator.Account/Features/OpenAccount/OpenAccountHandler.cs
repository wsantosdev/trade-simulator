using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Shared;

namespace TradeSimulator.Account.Features.OpenAccount;

// Inbound port.
public interface IOpenAccount
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class OpenAccountHandler(IAccountStore store, IAccountMetrics metrics) : IOpenAccount
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await store.OpenAsync(AccountId.New(), cancellationToken);
        metrics.OperationCompleted("open", null, result.IsError ? result.ErrorValue : null);
        return result;
    }
}
