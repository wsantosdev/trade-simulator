using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.ValueObjects;
using TradingSimulator.Orders.Processors.OrderTransition;

namespace TradingSimulator.Orders.Processors.AssetReservation;

// Throws only on infrastructure failures (the message must be redelivered);
// orders that are no longer pending submission are ignored by the transitions.
public sealed class AssetReservationProcessor(IOrderTransitions transitions, IOrdersMetrics metrics, ILogger<AssetReservationProcessor> logger)
{
    public Task BlockedAsync(OrderId orderId, string ticker, long quantity, CancellationToken cancellationToken)
    {
        logger.LogInformation("Assets blocked for order {OrderId}: {Quantity} {Ticker}.", orderId, quantity, ticker);
        metrics.SubmissionConfirmed("assets");
        return transitions.DispatchAsync(orderId, cancellationToken);
    }

    public Task FailedAsync(OrderId orderId, string? reason, CancellationToken cancellationToken)
    {
        metrics.SubmissionRejected("assets", reason);
        return transitions.RejectSubmissionAsync(orderId, string.IsNullOrWhiteSpace(reason) ? "AssetBlockFailed" : reason, cancellationToken);
    }
}
