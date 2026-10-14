using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingSimulator.Commons;

// Accepts a plain Guid string or the { "guid": "..." } identifier object published by other modules;
// always writes a plain string.
public sealed class FlexibleGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadGuid(ref reader);

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);

    public static Guid ReadGuid(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.TryGetGuid(out var text) ? text : throw new JsonException("Identifier Guid is invalid.");

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Identifiers must be a Guid string or an object containing a Guid.");

        Guid? guid = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Invalid identifier payload.");

            var name = reader.GetString();
            reader.Read();
            if (string.Equals(name, "guid", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var parsed))
                    throw new JsonException("Identifier Guid is invalid.");
                guid = parsed;
            }
            else
            {
                reader.Skip();
            }
        }

        return guid ?? throw new JsonException("Identifier payload must contain a Guid.");
    }
}
