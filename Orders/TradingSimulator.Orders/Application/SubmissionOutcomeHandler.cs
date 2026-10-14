using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Application;

// Applies the outcome of the internal validation (Account funds / Portfolio assets) to an order pending submission:
// success dispatches it to the Exchange; failure rejects it locally (the Exchange never saw it).
public sealed class SubmissionOutcomeHandler(IOrderStore store, IOrderNotifier notifier, TimeProvider clock, ILogger<SubmissionOutcomeHandler> logger)
{
    private const int MaxAttempts = 5;

    public Task DispatchAsync(OrderId id, CancellationToken cancellationToken) =>
        ApplyAsync(id, "dispatch", (order, now) => order.Dispatch(now), cancellationToken);

    public Task RejectAsync(OrderId id, string reason, CancellationToken cancellationToken) =>
        ApplyAsync(id, $"rejection ({reason})", (order, now) => order.RejectSubmission(reason, now), cancellationToken);

    private async Task ApplyAsync(
        OrderId id, string action, Func<Order, DateTimeOffset, Result<Order, IOrderError>> apply, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var loaded = await store.GetAsync(id, cancellationToken);
            if (loaded.IsError)
            {
                logger.LogWarning("Discarded {Action} for order {OrderId}: {Error}.", action, id, loaded.ErrorValue.GetType().Name);
                return;
            }

            var before = OrderView.From(loaded.ResultValue);
            var applied = apply(loaded.ResultValue, clock.GetUtcNow());
            if (applied.IsError)
            {
                logger.LogInformation("Ignored {Action} for order {OrderId} in state {State}.", action, id, before.State);
                return;
            }

            var saved = await store.AppendAsync(applied.ResultValue, cancellationToken);
            if (!saved.IsError)
            {
                logger.LogInformation("Applied {Action} to order {OrderId}; state is {State}.", action, id, saved.ResultValue.State);
                var after = OrderView.From(saved.ResultValue);
                await notifier.OrderUpdatedAsync(after.AccountId, OrderChanges.Between(before, after), cancellationToken);
                return;
            }

            if (saved.ErrorValue is not ConcurrencyConflict)
                throw new InvalidOperationException($"Could not persist {action} for order {id}: {saved.ErrorValue.GetType().Name}.");
        }

        throw new InvalidOperationException($"Could not persist {action} for order {id} after {MaxAttempts} concurrent attempts.");
    }
}
