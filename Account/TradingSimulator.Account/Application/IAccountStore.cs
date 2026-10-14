using TradingSimulator.Account.Domain;
using Moonad;

namespace TradingSimulator.Account.Application;

public interface IAccountStore
{
    Task<Result<Domain.Account, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken);
    Task<Result<Domain.Account, IAccountError>> ExecuteAsync(
        AccountId id,
        Func<Domain.Account, Result<Domain.Account, IAccountError>> operation,
        CancellationToken cancellationToken);
    Task<Result<Domain.Account, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken);
}
