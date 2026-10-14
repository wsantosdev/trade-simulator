using System.Text.Json;
using TradingSimulator.Orders.Infrastructure.Serialization;
using TradingSimulator.Orders.Processors.OrderTransition;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

// Parses the exchange envelope and converts the payload to a neutral ExchangeEvent;
// malformed, unsupported and invalid messages are logged and discarded.
public sealed class ExchangeEventMessageHandler(OrderTransitionProcessor processor, ILogger<ExchangeEventMessageHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        EventEnvelope<JsonElement>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, OrderJson.Options);
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

        ExchangeEvent? exchangeEvent;
        try
        {
            exchangeEvent = Convert(envelope.EventType, envelope.Event);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded exchange event {EventType} with an invalid payload.", envelope.EventType);
            return;
        }

        if (exchangeEvent is null)
        {
            logger.LogWarning("Discarded unsupported exchange event {EventType}.", envelope.EventType);
            return;
        }

        await processor.ProcessAsync(exchangeEvent, cancellationToken);
    }

    private static ExchangeEvent? Convert(string eventType, JsonElement payload)
    {
        T Read<T>() => payload.Deserialize<T>(OrderJson.Options) ?? throw new JsonException("Empty payload.");

        switch (eventType)
        {
            case "OrderAcked":
                return new ExchangeAcked(Read<OrderAckedMessage>().OrderId);
            case "OrderRejected":
                return new ExchangeRejected(Read<OrderRejectedMessage>().OrderId);
            case "OrderPartiallyFilled":
            {
                var m = Read<OrderPartiallyFilledMessage>();
                return new ExchangePartiallyFilled(m.OrderId, m.ExecutedQuantity, m.RemainingQuantity, m.ExecutionPrice);
            }
            case "OrderFilled":
            {
                var m = Read<OrderFilledMessage>();
                return new ExchangeFilled(m.OrderId, m.ExecutedQuantity, m.ExecutionPrice);
            }
            case "OrderCancelled":
            {
                var m = Read<OrderCancelledMessage>();
                return new ExchangeCancelled(m.OrderId, m.ExecutedQuantity, m.RemainingQuantity);
            }
            case "OrderReplaced":
            {
                var m = Read<OrderReplacedMessage>();
                return new ExchangeReplaced(m.OrderId, m.ExecutedQuantity, m.RemainingQuantity);
            }
            case "OrderCancelRejected":
                return new ExchangeCancelRejected(Read<OrderCancelRejectedMessage>().OrderId);
            case "OrderReplaceRejected":
                return new ExchangeReplaceRejected(Read<OrderReplaceRejectedMessage>().OrderId);
            default:
                return null;
        }
    }
}
