using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Infrastructure.Serialization;

// Same wire format as the Orders module: written as { "guid": "..." }, read from that object or from a plain Guid string.
public sealed class OrderIdJsonConverter : JsonConverter<OrderId>
{
    public override OrderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Guid? guid = null;
        if (reader.TokenType == JsonTokenType.String)
        {
            guid = reader.TryGetGuid(out var text) ? text : null;
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Invalid order identifier payload.");

                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "guid", StringComparison.OrdinalIgnoreCase)
                    && reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var parsed))
                    guid = parsed;
                else
                    reader.Skip();
            }
        }

        if (guid is null)
            throw new JsonException("Order identifier must be a non-empty UUID version 7 value.");

        var id = OrderId.TryCreate(guid.Value);
        return id.IsError
            ? throw new JsonException("Order identifier must be a non-empty UUID version 7 value.")
            : id.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, OrderId value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("guid", value.Guid);
        writer.WriteEndObject();
    }
}
