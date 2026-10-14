using System.Text.Json;
using Moonad;
using TradingSimulator.Orders.Infrastructure.Serialization;
using TradingSimulator.Orders.Processors.AssetReservation;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

// Parses the Portfolio reservation envelope; malformed messages are logged and discarded.
public sealed class AssetReservationMessageHandler(AssetReservationProcessor processor, ILogger<AssetReservationMessageHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        EventEnvelope<Result<AssetsBlocked, AssetBlockError>>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<Result<AssetsBlocked, AssetBlockError>>>(body, OrderJson.Options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded a malformed asset reservation message.");
            return;
        }

        if (envelope is null)
        {
            logger.LogWarning("Discarded an empty asset reservation message.");
            return;
        }

        var result = envelope.Event;
        if (!result.IsError)
        {
            await processor.BlockedAsync(result.ResultValue.OrderId, result.ResultValue.Ticker, result.ResultValue.Quantity, cancellationToken);
            return;
        }

        var error = result.ErrorValue;
        if (error.OrderId.Guid == Guid.Empty)
        {
            logger.LogWarning("Discarded an asset reservation error without an order id.");
            return;
        }

        await processor.FailedAsync(error.OrderId, error.Kind, cancellationToken);
    }
}
