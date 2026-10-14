using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSimulator.Account.Domain.Entities;
using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Infrastructure.Serialization;

public sealed class AccountIdJsonConverter : JsonConverter<AccountId>
{
    public override AccountId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ParseAccountId(reader.GetString());

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Account identifiers must be serialized as an object containing a Guid.");

        Guid? guid = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Invalid account identifier payload.");

            var propertyName = reader.GetString();
            if (!reader.Read())
                throw new JsonException("Account identifier payload ended unexpectedly.");

            if (string.Equals(propertyName, "guid", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var parsedGuid))
                    throw new JsonException("Account identifier Guid is invalid.");
                guid = parsedGuid;
            }
            else
            {
                reader.Skip();
            }
        }

        if (guid is null)
            throw new JsonException("Account identifier payload must contain a Guid.");

        return Convert(guid.Value);
    }

    public override void Write(Utf8JsonWriter writer, AccountId value, JsonSerializerOptions options)
    {
        if (value.Guid == Guid.Empty || value.Guid.Version != 7)
            throw new JsonException("Cannot serialize an account identifier that is not a non-empty UUID version 7 value.");

        writer.WriteStartObject();
        writer.WriteString("guid", value.Guid);
        writer.WriteEndObject();
    }

    private static AccountId ParseAccountId(string? value)
    {
        if (!Guid.TryParse(value, out var guid))
            throw new JsonException("Account identifier Guid is invalid.");
        return Convert(guid);
    }

    private static AccountId Convert(Guid guid)
    {
        var result = AccountId.TryCreate(guid);
        if (result.IsError)
            throw new JsonException("Account identifier must be a non-empty UUID version 7 value.");
        return result.ResultValue;
    }
}
