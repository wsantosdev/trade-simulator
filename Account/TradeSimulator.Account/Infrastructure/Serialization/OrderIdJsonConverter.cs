using System.Text.Json;
using System.Text.Json.Serialization;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Infrastructure.Serialization;

// Written as { "guid": "..." } like the other identifiers; read from that object or from a plain Guid string.
public sealed class OrderIdJsonConverter : JsonConverter<OrderId>
{
    public override OrderId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = OrderId.TryCreate(FlexibleGuidJsonConverter.ReadGuid(ref reader));
        return result.IsError
            ? throw new JsonException("Order identifier must be a non-empty UUID version 7 value.")
            : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, OrderId value, JsonSerializerOptions options)
    {
        if (value.Guid == Guid.Empty || value.Guid.Version != 7)
            throw new JsonException("Cannot serialize an order identifier that is not a non-empty UUID version 7 value.");

        writer.WriteStartObject();
        writer.WriteString("guid", value.Guid);
        writer.WriteEndObject();
    }
}
