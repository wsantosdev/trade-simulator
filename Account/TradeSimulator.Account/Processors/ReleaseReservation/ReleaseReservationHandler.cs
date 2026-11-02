using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Processors.ReleaseReservation;

// Inbound port.
public interface IReleaseReservation
{
    // Idempotent: an unknown or already closed reservation changes nothing.
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid reservationId, CancellationToken cancellationToken);
}

public sealed class ReleaseReservationHandler(IAccountStore store, IAccountMetrics metrics) : IReleaseReservation
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var id = ReservationId.TryCreate(reservationId);
        var result = id.IsError
            ? Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue)
            : await ReleaseAsync(id.ResultValue, cancellationToken);
        metrics.OperationCompleted("release", null, result.IsError ? result.ErrorValue : null);
        return result;
    }

    private async Task<Result<Domain.Entities.Account, IAccountError>> ReleaseAsync(ReservationId id, CancellationToken cancellationToken)
    {
        var account = await store.FindByReservationAsync(id, cancellationToken);
        return account.IsError
            ? Result<Domain.Entities.Account, IAccountError>.Error(account.ErrorValue)
            : await store.ExecuteAsync(account.ResultValue, current => current.CloseReservation(id), cancellationToken);
    }
}
