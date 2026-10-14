using System.Text.Json;
using Moonad;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.Enums;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Common;

// Translates the Orders module envelopes (OrderDispatched, OrderCancelRequested, OrderReplaceRequested) into engine commands.
public sealed class OrderCommandProcessor(OrderBookEngine engine)
{
    // Malformed messages (no event type, unknown type, unreadable order id) come back as errors and must be dropped, not retried.
    public async Task<Result<OrderCommand, IExchangeError>> ProcessAsync(
        string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var parsed = Parse(body, options);
        if (!parsed.IsError)
            await engine.HandleAsync(parsed.ResultValue, cancellationToken);
        return parsed;
    }

    public static Result<OrderCommand, IExchangeError> Parse(string body, JsonSerializerOptions options)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, options);
            if (envelope is null || envelope.Event.ValueKind != JsonValueKind.Object)
                return new MalformedMessage();

            var orderId = ReadOrderId(envelope.Event, options);
            if (orderId.IsError)
                return new MalformedMessage();

            var id = orderId.ResultValue;
            return envelope.EventType switch
            {
                "OrderDispatched" => ParseSubmit(id, envelope.Event),
                "OrderCancelRequested" => new CancelOrder(id),
                "OrderReplaceRequested" => ParseReplace(id, envelope.Event),
                _ => new MalformedMessage()
            };
        }
        catch (JsonException)
        {
            return new MalformedMessage();
        }
    }

    private static Result<OrderId, IExchangeError> ReadOrderId(JsonElement payload, JsonSerializerOptions options)
    {
        if (!payload.TryGetProperty("orderId", out var element))
            return new MalformedMessage();

        // Ids arrive as a Guid string or as { "guid": "..." }; the converter rejects anything that is not a UUID v7.
        var id = element.Deserialize<OrderId>(options);
        return id == default ? new InvalidOrderId() : id;
    }

    // A submission that identifies its order but is otherwise invalid is rejected (the Orders module is waiting for an answer).
    private static OrderCommand ParseSubmit(OrderId id, JsonElement payload)
    {
        var ticker = Ticker.TryCreate(StringOf(payload, "ticker"));
        var side = EnumOf<OrderSide>(payload, "side");
        var type = EnumOf<OrderType>(payload, "type");
        var quantity = payload.TryGetProperty("quantity", out var quantityElement) && quantityElement.TryGetInt64(out var quantityValue)
            ? Quantity.TryCreate(quantityValue)
            : new InvalidQuantity();
        if (ticker.IsError || side is null || type is null || quantity.IsError)
            return new RejectOrder(id);

        Price? price = null;
        if (payload.TryGetProperty("price", out var priceElement) && priceElement.ValueKind != JsonValueKind.Null)
        {
            var created = priceElement.TryGetDecimal(out var value) ? Price.TryCreate(value) : new InvalidPrice();
            if (created.IsError)
                return new RejectOrder(id);
            price = created.ResultValue;
        }

        if (type == OrderType.Limit && price is null)
            return new RejectOrder(id);
        return new SubmitOrder(id, ticker.ResultValue, side.Value, type.Value, quantity.ResultValue, type == OrderType.Market ? null : price);
    }

    // A replace without a valid price cannot be applied: the engine answers with OrderReplaceRejected.
    private static OrderCommand ParseReplace(OrderId id, JsonElement payload)
    {
        var price = payload.TryGetProperty("newPrice", out var element) && element.TryGetDecimal(out var value)
            ? Price.TryCreate(value)
            : new InvalidPrice();
        return price.IsError ? new ReplaceOrder(id, default) : new ReplaceOrder(id, price.ResultValue);
    }

    private static string? StringOf(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static T? EnumOf<T>(JsonElement payload, string name) where T : struct, Enum
    {
        var text = StringOf(payload, name);
        return text is not null && Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;
    }
}
