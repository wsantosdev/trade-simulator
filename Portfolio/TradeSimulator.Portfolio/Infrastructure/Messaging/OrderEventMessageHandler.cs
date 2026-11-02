using System.Text.Json;
using TradeSimulator.Portfolio.Domain.Enums;
using TradeSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

// Orders events: only the cancellation of a scheduled order is handled (it releases the blocked quantity).
public sealed class OrderEventMessageHandler(
    PortfolioCommandProcessor processor,
    ILogger<OrderEventMessageHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        var envelope = EnvelopeReader.TryRead(body, logger);
        if (envelope is null)
            return;

        if (envelope.EventType == PortfolioEventTypes.OrderScheduleCancelled)
        {
            await ReleaseScheduledAsync(envelope, cancellationToken);
            return;
        }

        // Sell orders are reserved synchronously by Orders over HTTP; nothing else is handled here.
        logger.LogDebug("Discarded order event {EventType}.", envelope.EventType);
    }

    // A scheduled order cancelled in Orders never reached the Exchange: release the whole reservation.
    private async Task ReleaseScheduledAsync(EventEnvelope<JsonElement> envelope, CancellationToken cancellationToken)
    {
        if (!EnvelopeReader.HasValidId(envelope))
        {
            logger.LogWarning("Discarded {EventType} with an invalid envelope id.", envelope.EventType);
            return;
        }

        Guid orderId;
        try
        {
            orderId = envelope.Event.Deserialize<OrderClosureMessage>(Serialization.PortfolioJson.Options)?.OrderId ?? Guid.Empty;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{EventType} {EnvelopeId} has an invalid payload; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        if (orderId == Guid.Empty)
        {
            logger.LogWarning("{EventType} {EnvelopeId} has no order id; discarded.", envelope.EventType, envelope.Id);
            return;
        }

        await processor.SettleOrderAsync(envelope.Id, orderId, 0, true, cancellationToken);
    }
}
