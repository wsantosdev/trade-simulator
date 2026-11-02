using Moonad;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Features.Shared;

// Outbound port: saves and loads accounts. It only stores events; the rules live in the Account entity.
public interface IAccountStore
{
    Task<Result<Domain.Entities.Account, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken);

    // Loads the account, runs the operation and saves the new events, one operation at a time per account.
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        AccountId id,
        Func<Domain.Entities.Account, Result<Domain.Entities.Account, IAccountError>> operation,
        CancellationToken cancellationToken);

    Task<Result<Domain.Entities.Account, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken);

    // Which account holds the reservation (found in its FundsReserved event).
    Task<Result<AccountId, IAccountError>> FindByReservationAsync(ReservationId id, CancellationToken cancellationToken);
}
