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
}
