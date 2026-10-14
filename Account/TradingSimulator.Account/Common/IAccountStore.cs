using Moonad;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Common;

public interface IAccountStore
{
    Task<Result<Domain.Entities.Account, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken);
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        AccountId id,
        Func<Domain.Entities.Account, Result<Domain.Entities.Account, IAccountError>> operation,
        CancellationToken cancellationToken);
    Task<Result<Domain.Entities.Account, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken);
}
