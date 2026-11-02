using TradeSimulator.Commons.Messaging;
using TradeSimulator.Portfolio.Processors.ReleaseReservation;
using TradeSimulator.Portfolio.Processors.SettleReservation;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

// Settles the assets an order reserved as its executions are announced. The order id is the reservation id.
// Portfolio receives the events of every order, so unknown reservations are expected and simply ignored.
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

        if (message.Kind == OrderExecutionKind.Release)
        {
            await releaseReservation.ExecuteAsync(message.OrderId, cancellationToken);
            return;
        }

        var result = await settleReservation.ExecuteAsync(
            message.OrderId, message.EventId, message.Quantity,
            close: message.Kind == OrderExecutionKind.FinalFill, cancellationToken);
        if (result != SettlementResult.Settled)
            logger.LogDebug("Order event {EventId} ignored: {Result}.", message.EventId, result);
    }
}
