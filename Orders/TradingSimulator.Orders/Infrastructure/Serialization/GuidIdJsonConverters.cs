using System.Text.Json;
using System.Text.Json.Serialization;
using Moonad;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Infrastructure.Serialization;

public abstract class GuidIdJsonConverter<TId> : JsonConverter<TId> where TId : struct
{
    protected abstract Result<TId, IOrderError> Create(Guid value);
    protected abstract Guid GuidOf(TId id);

    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (!reader.TryGetGuid(out var text))
                throw new JsonException("Identifier Guid is invalid.");
            return Convert(text);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Identifiers must be serialized as an object containing a Guid.");

        Guid? guid = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Invalid identifier payload.");

            var propertyName = reader.GetString();
            if (!reader.Read())
                throw new JsonException("Identifier payload ended unexpectedly.");

            if (string.Equals(propertyName, "guid", StringComparison.OrdinalIgnoreCase))
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

        return guid is null
            ? throw new JsonException("Identifier payload must contain a Guid.")
            : Convert(guid.Value);
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        var guid = GuidOf(value);
        if (guid == Guid.Empty || guid.Version != 7)
            throw new JsonException("Cannot serialize an identifier that is not a non-empty UUID version 7 value.");

        writer.WriteStartObject();
        writer.WriteString("guid", guid);
        writer.WriteEndObject();
    }

    private TId Convert(Guid guid)
    {
        var result = Create(guid);
        return result.IsError
            ? throw new JsonException("Identifier must be a non-empty UUID version 7 value.")
            : result.ResultValue;
    }
}

public sealed class OrderIdJsonConverter : GuidIdJsonConverter<OrderId>
{
    protected override Result<OrderId, IOrderError> Create(Guid value) => OrderId.TryCreate(value);
    protected override Guid GuidOf(OrderId id) => id.Guid;
}

public sealed class AccountIdJsonConverter : GuidIdJsonConverter<AccountId>
{
    protected override Result<AccountId, IOrderError> Create(Guid value) => AccountId.TryCreate(value);
    protected override Guid GuidOf(AccountId id) => id.Guid;
}
