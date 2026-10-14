using System.Text.Json;
using TradingSimulator.Commons;
using TradingSimulator.Portfolio.Infrastructure.Serialization;

namespace TradingSimulator.Portfolio.Infrastructure.Messaging;

internal static class EnvelopeReader
{
    // Null when the body is not JSON of an envelope or carries no event type.
    public static EventEnvelope<JsonElement>? TryRead(string body, ILogger logger)
    {
        EventEnvelope<JsonElement>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, PortfolioJson.Options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Received a malformed message.");
            return null;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            logger.LogWarning("Received a message without an event type.");
            return null;
        }

        return envelope;
    }

    public static bool HasValidId(EventEnvelope<JsonElement> envelope) =>
        envelope.Id != Guid.Empty && envelope.Id.Version == 7;

    public static T Read<T>(JsonElement payload) =>
        payload.Deserialize<T>(PortfolioJson.Options) ?? throw new JsonException("Empty payload.");
}
