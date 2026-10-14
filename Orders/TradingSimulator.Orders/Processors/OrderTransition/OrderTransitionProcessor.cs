using TradingSimulator.Orders.Common;

namespace TradingSimulator.Orders.Processors.OrderTransition;

// Applies what the exchange reported to the order. Throws only on infrastructure failures
// (the message must be redelivered); business rejections are handled by the transitions.
public sealed class OrderTransitionProcessor(IOrderTransitions transitions)
{
    public Task ProcessAsync(ExchangeEvent exchangeEvent, CancellationToken cancellationToken) => exchangeEvent switch
    {
        ExchangeAcked e => transitions.AckAsync(e.OrderId, cancellationToken),
        ExchangeRejected e => transitions.RejectAsync(e.OrderId, cancellationToken),
        ExchangePartiallyFilled e => transitions.PartiallyFillAsync(e.OrderId, e.ExecutedQuantity, e.RemainingQuantity, e.ExecutionPrice, cancellationToken),
        ExchangeFilled e => transitions.FillAsync(e.OrderId, e.ExecutedQuantity, e.ExecutionPrice, cancellationToken),
        ExchangeCancelled e => transitions.CancelAsync(e.OrderId, e.ExecutedQuantity, e.RemainingQuantity, cancellationToken),
        ExchangeReplaced e => transitions.ReplaceAsync(e.OrderId, e.ExecutedQuantity, e.RemainingQuantity, cancellationToken),
        ExchangeCancelRejected e => transitions.RejectCancelAsync(e.OrderId, cancellationToken),
        ExchangeReplaceRejected e => transitions.RejectReplaceAsync(e.OrderId, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(exchangeEvent))
    };
}
