using TradingSimulator.Orders.Domain.ValueObjects;
using TradingSimulator.Orders.Processors.OrderTransition;

namespace TradingSimulator.Orders.Processors.FundsReservation;

// Throws only on infrastructure failures (the message must be redelivered);
// orders that are no longer pending submission are ignored by the transitions.
public sealed class FundsReservationProcessor(IOrderTransitions transitions, ILogger<FundsReservationProcessor> logger)
{
    public Task ReservedAsync(OrderId orderId, decimal amount, CancellationToken cancellationToken)
    {
        logger.LogInformation("Funds reserved for order {OrderId}: {Amount}.", orderId, amount);
        return transitions.DispatchAsync(orderId, cancellationToken);
    }

    public Task FailedAsync(OrderId orderId, string? reason, CancellationToken cancellationToken) =>
        transitions.RejectSubmissionAsync(orderId, string.IsNullOrWhiteSpace(reason) ? "FundsReservationFailed" : reason, cancellationToken);
}
