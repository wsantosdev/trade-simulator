using Moonad;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Features.Shared;

namespace TradingSimulator.Account.Features.ReserveFunds;

// Inbound port.
public interface IReserveFunds
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken);
}

public sealed class ReserveFundsHandler(IAccountStore store) : IReserveFunds
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue);

        return await store.ExecuteAsync(id.ResultValue, current => current.Reserve(amount), cancellationToken);
    }
}
