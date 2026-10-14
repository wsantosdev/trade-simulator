using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSimulator.Portfolio.Domain;

namespace TradingSimulator.Portfolio.Infrastructure;

public sealed class AccountIdJsonConverter : JsonConverter<AccountId>
{
    public override AccountId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = AccountId.TryCreate(FlexibleGuidJsonConverter.ReadGuid(ref reader));
        return result.IsError ? throw new JsonException("Account id must be a non-empty UUID version 7 value.") : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, AccountId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Guid);
}

public sealed class AssetIdJsonConverter : JsonConverter<AssetId>
{
    public override AssetId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = AssetId.TryCreate(FlexibleGuidJsonConverter.ReadGuid(ref reader));
        return result.IsError ? throw new JsonException("Asset id must be a non-empty UUID version 7 value.") : result.ResultValue;
    }

    public override void Write(Utf8JsonWriter writer, AssetId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Guid);
}
