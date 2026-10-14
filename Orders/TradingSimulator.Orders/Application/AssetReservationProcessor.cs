using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;

namespace TradingSimulator.Orders.Application;

public sealed class AssetReservationProcessor(SubmissionOutcomeHandler outcomes, ILogger<AssetReservationProcessor> logger)
{
    // Throws only on infrastructure failures (the message must be redelivered);
    // malformed messages and orders that are no longer pending submission are logged and discarded.
    public async Task ProcessAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        EventEnvelope<Result<AssetsBlocked, AssetBlockError>>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<Result<AssetsBlocked, AssetBlockError>>>(body, options);
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
            logger.LogInformation("Assets blocked for order {OrderId}: {Quantity} {Ticker}.",
                result.ResultValue.OrderId, result.ResultValue.Quantity, result.ResultValue.Ticker);
            await outcomes.DispatchAsync(result.ResultValue.OrderId, cancellationToken);
            return;
        }

        var error = result.ErrorValue;
        if (error.OrderId.Guid == Guid.Empty)
        {
            logger.LogWarning("Discarded an asset reservation error without an order id.");
            return;
        }

        var reason = string.IsNullOrWhiteSpace(error.Kind) ? "AssetBlockFailed" : error.Kind;
        await outcomes.RejectAsync(error.OrderId, reason, cancellationToken);
    }
}
