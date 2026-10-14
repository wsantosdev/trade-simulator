using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Commons;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.Events;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed class ExchangeEventProcessor(IOrderTransitions transitions, ILogger<ExchangeEventProcessor> logger)
{
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
            var handled = await HandleAsync(envelope.EventType, envelope.Event, options, cancellationToken);
            if (!handled)
                logger.LogWarning("Discarded unsupported exchange event {EventType}.", envelope.EventType);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded exchange event {EventType} with an invalid payload.", envelope.EventType);
        }
    }

    private async Task<bool> HandleAsync(string eventType, JsonElement payload, JsonSerializerOptions options, CancellationToken ct)
    {
        T Read<T>() => payload.Deserialize<T>(options) ?? throw new JsonException("Empty payload.");

        switch (eventType)
        {
            case "OrderAcked":
                await transitions.AckAsync(Read<OrderAckedMessage>().OrderId, ct);
                return true;
            case "OrderRejected":
                await transitions.RejectAsync(Read<OrderRejectedMessage>().OrderId, ct);
                return true;
            case "OrderPartiallyFilled":
            {
                var m = Read<OrderPartiallyFilledMessage>();
                await transitions.PartiallyFillAsync(m.OrderId, m.ExecutedQuantity, m.RemainingQuantity, m.ExecutionPrice, ct);
                return true;
            }
            case "OrderFilled":
            {
                var m = Read<OrderFilledMessage>();
                await transitions.FillAsync(m.OrderId, m.ExecutedQuantity, m.ExecutionPrice, ct);
                return true;
            }
            case "OrderCancelled":
            {
                var m = Read<OrderCancelledMessage>();
                await transitions.CancelAsync(m.OrderId, m.ExecutedQuantity, m.RemainingQuantity, ct);
                return true;
            }
            case "OrderReplaced":
            {
                var m = Read<OrderReplacedMessage>();
                await transitions.ReplaceAsync(m.OrderId, m.ExecutedQuantity, m.RemainingQuantity, ct);
                return true;
            }
            case "OrderCancelRejected":
                await transitions.RejectCancelAsync(Read<OrderCancelRejectedMessage>().OrderId, ct);
                return true;
            case "OrderReplaceRejected":
                await transitions.RejectReplaceAsync(Read<OrderReplaceRejectedMessage>().OrderId, ct);
                return true;
            default:
                return false;
        }
    }
}
