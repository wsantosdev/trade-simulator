using System.Text.Json;
using TradingSimulator.Portfolio.Domain.Enums;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradingSimulator.Portfolio.Infrastructure.Messaging;

// Orders events: a sell OrderSubmitted reserves the quantity; everything else (buy orders included) is discarded.
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

        if (envelope.EventType != PortfolioEventTypes.OrderSubmitted)
        {
            logger.LogDebug("Discarded order event {EventType}.", envelope.EventType);
            return;
        }

        if (!EnvelopeReader.HasValidId(envelope))
        {
            logger.LogWarning("Discarded {EventType} with an invalid envelope id.", envelope.EventType);
            return;
        }

        OrderSubmittedMessage submitted;
        try
        {
            submitted = EnvelopeReader.Read<OrderSubmittedMessage>(envelope.Event);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Command {EventType} {EnvelopeId} has an invalid payload.", envelope.EventType, envelope.Id);
            return;
        }

        if (submitted.Side != OrderSide.Sell)
        {
            logger.LogDebug("Discarded buy order {EnvelopeId}.", envelope.Id);
            return;
        }

        await processor.ReserveAsync(
            envelope.Id, new SellOrder(submitted.OrderId, submitted.AccountId, submitted.Ticker, submitted.Quantity), cancellationToken);
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
