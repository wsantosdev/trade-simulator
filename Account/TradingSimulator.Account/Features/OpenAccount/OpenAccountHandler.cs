using Moonad;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Features.Shared;

namespace TradingSimulator.Account.Features.OpenAccount;

// Inbound port.
public interface IOpenAccount
{
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class OpenAccountHandler(IAccountStore store) : IOpenAccount
{
    public Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(CancellationToken cancellationToken) =>
        store.OpenAsync(AccountId.New(), cancellationToken);
}
