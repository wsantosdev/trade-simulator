using System.Text.Json;
using System.Text.Json.Serialization;
using Moonad;

namespace TradingSimulator.Commons;

// Wire format: { "isError": false, "result": {...} } or { "isError": true, "error": {...} }.
public sealed class ResultJsonConverter<TResult, TError> : JsonConverter<Result<TResult, TError>>
    where TResult : notnull
    where TError : notnull
{
    public override Result<TResult, TError> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("isError", out var isError) || isError.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("Result payload must contain isError.");

        if (isError.GetBoolean())
        {
            var error = root.GetProperty("error").Deserialize<TError>(options) ?? throw new JsonException("Empty error.");
            return Result<TResult, TError>.Error(error);
        }

        var value = root.GetProperty("result").Deserialize<TResult>(options) ?? throw new JsonException("Empty result.");
        return Result<TResult, TError>.Ok(value);
    }

    public override void Write(Utf8JsonWriter writer, Result<TResult, TError> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isError", value.IsError);
        if (value.IsError)
        {
            writer.WritePropertyName("error");
            JsonSerializer.Serialize(writer, value.ErrorValue, options);
        }
        else
        {
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, value.ResultValue, options);
        }

        writer.WriteEndObject();
    }
}
