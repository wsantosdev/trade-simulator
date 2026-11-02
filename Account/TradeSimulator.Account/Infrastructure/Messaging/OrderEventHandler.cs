using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Processors.SettleReservation;
using TradeSimulator.Account.Processors.ReleaseReservation;
using TradeSimulator.Commons.Messaging;

namespace TradeSimulator.Account.Infrastructure.Messaging;

// Settles the funds an order reserved as its executions are announced. The order id is the reservation id.
// Account receives the events of every order, so unknown reservations are expected and simply ignored.
public sealed class OrderEventHandler(
    ISettleReservation settleReservation,
    IReleaseReservation releaseReservation,
    ILogger<OrderEventHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var message = OrderExecutionEvent.TryParse(body);
        if (message is null)
        {
            logger.LogWarning("Discarded a malformed order event.");
            return;
        }

        var result = message.Kind == OrderExecutionKind.Release
            ? await releaseReservation.ExecuteAsync(message.OrderId, cancellationToken)
            : await settleReservation.ExecuteAsync(
                message.OrderId, message.EventId, message.Quantity, message.Price,
                close: message.Kind == OrderExecutionKind.FinalFill, cancellationToken);

        if (!result.IsError)
            return;
        if (result.ErrorValue is ReservationNotFound or InvalidReservationId or InvalidDebitAmount)
            logger.LogDebug("Order event {EventId} ignored: {Error}.", message.EventId, result.ErrorValue);
        else
            throw new InvalidOperationException($"Order event {message.EventId} could not be applied: {result.ErrorValue}.");
    }
}
