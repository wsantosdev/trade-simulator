using Moonad;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Processors.SettleReservation;

// Inbound port.
public interface ISettleReservation
{
    // Idempotent by operation id. Debits executed quantity x execution price (up to the remaining amount);
    // when closing, the leftover (e.g. the order executed at a better price) goes back to the available balance.
    Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        Guid reservationId, Guid operationId, long executedQuantity, decimal executionPrice, bool close, CancellationToken cancellationToken);
}

public sealed class SettleReservationHandler(IAccountStore store, IAccountMetrics metrics) : ISettleReservation
{
    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        Guid reservationId, Guid operationId, long executedQuantity, decimal executionPrice, bool close, CancellationToken cancellationToken)
    {
        var amount = Math.Round(executedQuantity * executionPrice, 2, MidpointRounding.AwayFromZero);
        var id = ReservationId.TryCreate(reservationId);
        var result = id.IsError
            ? Result<Domain.Entities.Account, IAccountError>.Error(id.ErrorValue)
            : amount <= 0
                ? Result<Domain.Entities.Account, IAccountError>.Error(new InvalidDebitAmount())
                : await SettleAsync(id.ResultValue, operationId, amount, close, cancellationToken);
        metrics.OperationCompleted("debit", amount, result.IsError ? result.ErrorValue : null);
        return result;
    }

    private async Task<Result<Domain.Entities.Account, IAccountError>> SettleAsync(
        ReservationId id, Guid operationId, decimal amount, bool close, CancellationToken cancellationToken)
    {
        var account = await store.FindByReservationAsync(id, cancellationToken);
        return account.IsError
            ? Result<Domain.Entities.Account, IAccountError>.Error(account.ErrorValue)
            : await store.ExecuteAsync(account.ResultValue, current => current.Settle(id, operationId, amount, close), cancellationToken);
    }
}
