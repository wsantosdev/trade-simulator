using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;

namespace TradingSimulator.Orders.Application;

public sealed class FundsReservationProcessor(SubmissionOutcomeHandler outcomes, ILogger<FundsReservationProcessor> logger)
{
    // Throws only on infrastructure failures (the message must be redelivered);
    // malformed messages and orders that are no longer pending submission are logged and discarded.
    public async Task ProcessAsync(string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        EventEnvelope<Result<FundsReserved, FundsReservationError>>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<Result<FundsReserved, FundsReservationError>>>(body, options);
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
            logger.LogInformation("Funds reserved for order {OrderId}: {Amount}.", result.ResultValue.OrderId, result.ResultValue.Amount);
            await outcomes.DispatchAsync(result.ResultValue.OrderId, cancellationToken);
            return;
        }

        var error = result.ErrorValue;
        if (error.OrderId.Guid == Guid.Empty)
        {
            logger.LogWarning("Discarded a funds reservation error without an order id.");
            return;
        }

        var reason = string.IsNullOrWhiteSpace(error.Kind) ? "FundsReservationFailed" : error.Kind;
        await outcomes.RejectAsync(error.OrderId, reason, cancellationToken);
    }
}
