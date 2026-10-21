using TradeSimulator.Orders.Common;
using Microsoft.Extensions.Logging;
using Moonad;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Events;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Processors.OrderTransition;

// Inbound port: state changes caused by other systems (Exchange, Account, Portfolio) on an existing order.
// Implementations never throw for business outcomes; they throw only on infrastructure failures (redelivery).
public interface IOrderTransitions
{
    Task DispatchAsync(OrderId id, CancellationToken cancellationToken);
    Task RejectSubmissionAsync(OrderId id, string reason, CancellationToken cancellationToken);
    Task AckAsync(OrderId id, CancellationToken cancellationToken);
    Task RejectAsync(OrderId id, string? reason, CancellationToken cancellationToken);
    Task PartiallyFillAsync(OrderId id, long executedQuantity, long remainingQuantity, decimal executionPrice, CancellationToken cancellationToken);
    Task FillAsync(OrderId id, long executedQuantity, decimal executionPrice, CancellationToken cancellationToken);
    Task CancelAsync(OrderId id, long executedQuantity, long remainingQuantity, CancellationToken cancellationToken);
    Task ReplaceAsync(OrderId id, long executedQuantity, long remainingQuantity, CancellationToken cancellationToken);
    Task RejectCancelAsync(OrderId id, CancellationToken cancellationToken);
    Task RejectReplaceAsync(OrderId id, CancellationToken cancellationToken);
}

public sealed class OrderTransitions(
    IOrderStore store, IOrderNotifier notifier, TimeProvider clock, IMarketHours marketHours, ILogger<OrderTransitions> logger)
    : IOrderTransitions
{
    private const int MaxAttempts = 5;

    // With the market closed the order is scheduled instead of sent; ScheduledOrderDispatcher dispatches it at the opening.
    public Task DispatchAsync(OrderId id, CancellationToken cancellationToken) =>
        marketHours.IsOpen()
            ? ApplyAsync(id, "dispatch", (o, now) => o.Dispatch(now), cancellationToken)
            : ApplyAsync(id, "scheduling", (o, now) => o.Schedule(now), cancellationToken);

    public Task RejectSubmissionAsync(OrderId id, string reason, CancellationToken cancellationToken) =>
        ApplyAsync(id, $"rejection ({reason})", (o, now) => o.RejectSubmission(reason, now), cancellationToken);

    public Task AckAsync(OrderId id, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderAcked", (o, now) => o.Ack(now), cancellationToken);

    public Task RejectAsync(OrderId id, string? reason, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderRejected", (o, now) => o.Reject(now, reason), cancellationToken);

    public Task PartiallyFillAsync(OrderId id, long executedQuantity, long remainingQuantity, decimal executionPrice, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderPartiallyFilled", (o, now) => o.PartiallyFill(executedQuantity, remainingQuantity, executionPrice, now), cancellationToken);

    public Task FillAsync(OrderId id, long executedQuantity, decimal executionPrice, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderFilled", (o, now) => o.Fill(executedQuantity, executionPrice, now), cancellationToken);

    public Task CancelAsync(OrderId id, long executedQuantity, long remainingQuantity, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderCancelled", (o, now) => o.Cancel(executedQuantity, remainingQuantity, now), cancellationToken);

    public Task ReplaceAsync(OrderId id, long executedQuantity, long remainingQuantity, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderReplaced", (o, now) => o.Replace(executedQuantity, remainingQuantity, now), cancellationToken);

    public Task RejectCancelAsync(OrderId id, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderCancelRejected", (o, now) => o.RejectCancel(now), cancellationToken);

    public Task RejectReplaceAsync(OrderId id, CancellationToken cancellationToken) =>
        ApplyAsync(id, "OrderReplaceRejected", (o, now) => o.RejectReplace(now), cancellationToken);

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
                logger.LogInformation("Ignored {Action} for order {OrderId} in state {State}: {Error}.",
                    action, id, before.State, applied.ErrorValue.GetType().Name);
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
