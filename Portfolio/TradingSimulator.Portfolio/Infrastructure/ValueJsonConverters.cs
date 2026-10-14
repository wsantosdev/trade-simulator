using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Infrastructure;

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

public sealed class QuantityJsonConverter : JsonConverter<Quantity>
{
    public override Quantity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = Quantity.TryCreate(reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var value) ? value : 0);
        return result.IsError ? throw new JsonException("Quantity is invalid.") : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, Quantity value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}
