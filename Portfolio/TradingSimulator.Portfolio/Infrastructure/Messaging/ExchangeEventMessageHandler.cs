using System.Text.Json;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradingSimulator.Portfolio.Infrastructure.Messaging;

// Exchange events: fills settle the executed quantity, the final fill, cancel and reject release what is left.
public sealed class ExchangeEventMessageHandler(
    PortfolioCommandProcessor processor,
    ILogger<ExchangeEventMessageHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.TryRead(body, logger);
        if (envelope is null)
            return;

        if (!IsExchangeEvent(envelope.EventType))
        {
            logger.LogDebug("Discarded order event {EventType}.", envelope.EventType);
            return;
        }

        if (!EnvelopeReader.HasValidId(envelope))
        {
            logger.LogWarning("Discarded {EventType} with an invalid envelope id.", envelope.EventType);
            return;
        }

        var isFill = envelope.EventType is PortfolioEventTypes.OrderPartiallyFilled or PortfolioEventTypes.OrderFilled;
        Guid orderId;
        long executed = 0;
        try
        {
            if (isFill)
            {
                var fill = envelope.Event.Deserialize<OrderFillMessage>(Serialization.PortfolioJson.Options);
                orderId = fill?.OrderId ?? Guid.Empty;
                executed = Math.Max(0, fill?.ExecutedQuantity ?? 0);
            }
            else
            {
                orderId = envelope.Event.Deserialize<OrderClosureMessage>(Serialization.PortfolioJson.Options)?.OrderId ?? Guid.Empty;
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{EventType} {EnvelopeId} has an invalid payload; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        if (orderId == Guid.Empty)
        {
            logger.LogWarning("{EventType} {EnvelopeId} has no valid order id; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        var final = envelope.EventType != PortfolioEventTypes.OrderPartiallyFilled;
        await processor.SettleOrderAsync(envelope.Id, orderId, executed, final, cancellationToken);
    }

    private static bool IsExchangeEvent(string eventType) =>
        eventType is PortfolioEventTypes.OrderPartiallyFilled or PortfolioEventTypes.OrderFilled
            or PortfolioEventTypes.OrderCancelled or PortfolioEventTypes.OrderRejected;
}
