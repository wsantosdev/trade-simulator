using Moonad;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Processors.OrderFunds;

namespace TradeSimulator.Account.Infrastructure.Redis;

// Deposits, withdrawals and manual reservations change the balance behind the gate's back: drop the cached copy after them.
public sealed class InvalidatingAccountStore(IAccountStore inner, IFundsGate gate) : IAccountStore
{
    public Task<Result<Domain.Entities.Account, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken) =>
        inner.OpenAsync(id, cancellationToken);

    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        AccountId id,
        Func<Domain.Entities.Account, Result<Domain.Entities.Account, IAccountError>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.ExecuteAsync(id, operation, cancellationToken);
        }
        finally
        {
            await gate.InvalidateAsync(id, CancellationToken.None);
        }
    }

    public Task<Result<Domain.Entities.Account, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken) =>
        inner.GetAsync(id, cancellationToken);
}
