using System.Text.Json;
using System.Text.Json.Serialization;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Infrastructure.Serialization;

// Written as { "guid": "..." } like the other identifiers; read from that object or from a plain Guid string.
public sealed class ReservationIdJsonConverter : JsonConverter<ReservationId>
{
    public override ReservationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = ReservationId.TryCreate(FlexibleGuidJsonConverter.ReadGuid(ref reader));
        return result.IsError
            ? throw new JsonException("Reservation identifier must be a non-empty UUID version 7 value.")
            : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, ReservationId value, JsonSerializerOptions options)
    {
        if (value.Guid == Guid.Empty || value.Guid.Version != 7)
            throw new JsonException("Cannot serialize a reservation identifier that is not a non-empty UUID version 7 value.");

        writer.WriteStartObject();
        writer.WriteString("guid", value.Guid);
        writer.WriteEndObject();
    }
}
