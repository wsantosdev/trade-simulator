using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Commons;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Application;

public sealed class ExchangeEventProcessor(IOrderStore store, IOrderNotifier notifier, TimeProvider clock, ILogger<ExchangeEventProcessor> logger)
{
    private const int MaxAttempts = 5;

    // Throws only on infrastructure failures (the message must be redelivered);
    // business rejections are logged and discarded.
    public async Task ProcessAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        EventEnvelope<JsonElement>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded a malformed exchange message.");
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            logger.LogWarning("Discarded an exchange message without an event type.");
            return;
        }

        try
        {
            var handler = Resolve(envelope.EventType, envelope.Event, options);
            if (handler is null)
            {
                logger.LogWarning("Discarded unsupported exchange event {EventType}.", envelope.EventType);
                return;
            }

            await HandleAsync(envelope.EventType, handler.Value.Id, handler.Value.Apply, cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded exchange event {EventType} with an invalid payload.", envelope.EventType);
        }
    }

    private async Task HandleAsync(string eventType, OrderId id, Func<Order, DateTimeOffset, Result<Order, IOrderError>> apply, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var loaded = await store.GetAsync(id, cancellationToken);
            if (loaded.IsError)
            {
                logger.LogWarning("Discarded {EventType} for order {OrderId}: {Error}.", eventType, id, loaded.ErrorValue.GetType().Name);
                return;
            }

            var before = OrderView.From(loaded.ResultValue);
            var applied = apply(loaded.ResultValue, clock.GetUtcNow());
            if (applied.IsError)
            {
                logger.LogWarning("Discarded {EventType} for order {OrderId}: {Error}.", eventType, id, applied.ErrorValue.GetType().Name);
                return;
            }

            var saved = await store.AppendAsync(applied.ResultValue, cancellationToken);
            if (!saved.IsError)
            {
                logger.LogInformation("Applied {EventType} to order {OrderId}; state is {State}.", eventType, id, saved.ResultValue.State);
                var after = OrderView.From(saved.ResultValue);
                await notifier.OrderUpdatedAsync(after.AccountId, OrderChanges.Between(before, after), cancellationToken);
                return;
            }

            if (saved.ErrorValue is not ConcurrencyConflict)
                throw new InvalidOperationException($"Could not persist {eventType} for order {id}: {saved.ErrorValue.GetType().Name}.");
        }

        throw new InvalidOperationException($"Could not persist {eventType} for order {id} after {MaxAttempts} concurrent attempts.");
    }

    private static (OrderId Id, Func<Order, DateTimeOffset, Result<Order, IOrderError>> Apply)? Resolve(
        string eventType, JsonElement payload, JsonSerializerOptions options)
    {
        T Read<T>() => payload.Deserialize<T>(options) ?? throw new JsonException("Empty payload.");

        switch (eventType)
        {
            case "OrderAcked":
            {
                var m = Read<OrderAckedMessage>();
                return (m.OrderId, (o, now) => o.Ack(now));
            }
            case "OrderRejected":
            {
                var m = Read<OrderRejectedMessage>();
                return (m.OrderId, (o, now) => o.Reject(now));
            }
            case "OrderPartiallyFilled":
            {
                var m = Read<OrderPartiallyFilledMessage>();
                return (m.OrderId, (o, now) => o.PartiallyFill(m.ExecutedQuantity, m.RemainingQuantity, m.ExecutionPrice, now));
            }
            case "OrderFilled":
            {
                var m = Read<OrderFilledMessage>();
                return (m.OrderId, (o, now) => o.Fill(m.ExecutedQuantity, m.ExecutionPrice, now));
            }
            case "OrderCancelled":
            {
                var m = Read<OrderCancelledMessage>();
                return (m.OrderId, (o, now) => o.Cancel(m.ExecutedQuantity, m.RemainingQuantity, now));
            }
            case "OrderReplaced":
            {
                var m = Read<OrderReplacedMessage>();
                return (m.OrderId, (o, now) => o.Replace(m.ExecutedQuantity, m.RemainingQuantity, now));
            }
            case "OrderCancelRejected":
            {
                var m = Read<OrderCancelRejectedMessage>();
                return (m.OrderId, (o, now) => o.RejectCancel(now));
            }
            case "OrderReplaceRejected":
            {
                var m = Read<OrderReplaceRejectedMessage>();
                return (m.OrderId, (o, now) => o.RejectReplace(now));
            }
            default:
                return null;
        }
    }
}
