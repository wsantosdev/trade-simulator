using Moonad;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Features.Shared;

namespace TradingSimulator.Account.Features.GetAccount;

// Inbound port.
public interface IGetAccount
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, CancellationToken cancellationToken);
}

public sealed class GetAccountHandler(IAccountStore store) : IGetAccount
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue);

        return await store.GetAsync(id.ResultValue, cancellationToken);
    }
}
