using System.Text.Json;
using System.Text.Json.Serialization;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Infrastructure.Serialization;

public sealed class TickerJsonConverter : JsonConverter<Ticker>
{
    public override Ticker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = Ticker.TryCreate(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);
        return result.IsError ? throw new JsonException("Ticker is invalid.") : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, Ticker value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class RequestHashJsonConverter : JsonConverter<RequestHash>
{
    public override RequestHash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = RequestHash.TryCreate(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);
        return result.IsError ? throw new JsonException("Request hash is invalid.") : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, RequestHash value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
