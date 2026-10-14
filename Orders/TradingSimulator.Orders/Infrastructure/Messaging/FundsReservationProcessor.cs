using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed class FundsReservationProcessor(IOrderTransitions transitions, ILogger<FundsReservationProcessor> logger)
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
            await transitions.DispatchAsync(result.ResultValue.OrderId, cancellationToken);
            return;
        }

        var error = result.ErrorValue;
        if (error.OrderId.Guid == Guid.Empty)
        {
            logger.LogWarning("Discarded a funds reservation error without an order id.");
            return;
        }

        var reason = string.IsNullOrWhiteSpace(error.Kind) ? "FundsReservationFailed" : error.Kind;
        await transitions.RejectSubmissionAsync(error.OrderId, reason, cancellationToken);
    }
}
