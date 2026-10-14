using System.Text.Json;
using Moonad;
using TradingSimulator.Orders.Infrastructure.Serialization;
using TradingSimulator.Orders.Processors.FundsReservation;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

// Parses the Account reservation envelope; malformed messages are logged and discarded.
public sealed class FundsReservationMessageHandler(FundsReservationProcessor processor, ILogger<FundsReservationMessageHandler> logger)
{
    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        EventEnvelope<Result<FundsReserved, FundsReservationError>>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<Result<FundsReserved, FundsReservationError>>>(body, OrderJson.Options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Discarded a malformed funds reservation message.");
            return;
        }

        if (envelope is null)
        {
            logger.LogWarning("Discarded an empty funds reservation message.");
            return;
        }

        var result = envelope.Event;
        if (!result.IsError)
        {
            await processor.ReservedAsync(result.ResultValue.OrderId, result.ResultValue.Amount, cancellationToken);
            return;
        }

        var error = result.ErrorValue;
        if (error.OrderId.Guid == Guid.Empty)
        {
            logger.LogWarning("Discarded a funds reservation error without an order id.");
            return;
        }

        await processor.FailedAsync(error.OrderId, error.Kind, cancellationToken);
    }
}
