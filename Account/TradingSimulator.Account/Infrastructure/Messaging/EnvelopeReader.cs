using System.Text.Json;
using TradingSimulator.Account.Infrastructure.Serialization;

namespace TradingSimulator.Account.Infrastructure.Messaging;

public static class EnvelopeReader
{
    // Null (after logging) when the message is malformed and must be discarded.
    public static EventEnvelope<JsonElement>? Read(string body, ILogger logger)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, AccountJson.Options);
            if (envelope is not null && !string.IsNullOrWhiteSpace(envelope.EventType) &&
                envelope.Id != Guid.Empty && envelope.Id.Version == 7)
                return envelope;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Received a malformed message; discarded.");
            return null;
        }

        logger.LogWarning("Received a message without a valid envelope id or event type; discarded.");
        return null;
    }

    public static T? ReadPayload<T>(EventEnvelope<JsonElement> envelope, ILogger logger) where T : class
    {
        try
        {
            return envelope.Event.Deserialize<T>(AccountJson.Options);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "{EventType} {EnvelopeId} has an invalid payload.", envelope.EventType, envelope.Id);
            return null;
        }
    }
}
