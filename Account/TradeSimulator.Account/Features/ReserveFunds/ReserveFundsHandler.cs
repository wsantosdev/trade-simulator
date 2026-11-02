using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Features.ReserveFunds;

// Inbound port.
public interface IReserveFunds
{
    // Synchronous and idempotent by reservation id: repeating the call for the same account changes nothing.
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        Guid reservationId, Guid accountId, decimal amount, CancellationToken cancellationToken);
}

public sealed class ReserveFundsHandler(
    IAccountStore store,
    IAccountMetrics metrics) : IReserveFunds
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        Guid reservationId, Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var result = await ReserveAsync(reservationId, accountId, amount, cancellationToken);
        metrics.OperationCompleted("reserve", amount, result.IsError ? result.ErrorValue : null);
        return result;
    }

    private async Task<Result<Domain.Entities.Account, IAccountError>> ReserveAsync(
        Guid reservationGuid, Guid accountGuid, decimal amount, CancellationToken cancellationToken)
    {
        var reservationId = ReservationId.TryCreate(reservationGuid);
        if (reservationId.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(reservationId.ErrorValue);
        var accountId = AccountId.TryCreate(accountGuid);
        if (accountId.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(accountId.ErrorValue);
        if (amount <= 0)
            return Result<Domain.Entities.Account, IAccountError>.Error(new InvalidReservationAmount());

        var id = reservationId.ResultValue;
        var account = accountId.ResultValue;
        return await store.ExecuteAsync(account, current => current.Reserve(amount, id), cancellationToken);
    }
}
