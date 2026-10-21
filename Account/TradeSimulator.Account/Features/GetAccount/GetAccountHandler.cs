using Moonad;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Shared;

namespace TradeSimulator.Account.Features.GetAccount;

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
