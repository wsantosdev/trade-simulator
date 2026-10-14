using Moonad;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Common;

// Every operation records the envelope as processed in the same transaction as its effects.
public interface IOrderFundsStore
{
    // Reserves the amount for the order, stores the order reservation and queues the success response.
    // A business failure (insufficient funds, unknown account...) changes nothing and is returned as the error.
    Task<Result<OrderFundsOutcome, IAccountError>> ReserveAsync(
        Guid envelopeId,
        OrderId orderId,
        AccountId accountId,
        decimal amount,
        FundsReservationResponse success,
        CancellationToken cancellationToken);

    // Remembers the order as having no reservation and queues the failure response (when there is one).
    Task<OrderFundsOutcome> RejectAsync(
        Guid envelopeId,
        OrderId orderId,
        string reason,
        FundsReservationResponse? failure,
        CancellationToken cancellationToken);

    // Debits up to the remaining reservation; when final, the leftover goes back to the available balance.
    Task<Result<OrderFundsOutcome, IAccountError>> DebitAsync(
        Guid envelopeId,
        OrderId orderId,
        decimal amount,
        bool final,
        CancellationToken cancellationToken);

    // Returns the remaining reservation of the order to the available balance.
    Task<Result<OrderFundsOutcome, IAccountError>> ReleaseAsync(
        Guid envelopeId,
        OrderId orderId,
        CancellationToken cancellationToken);
}
