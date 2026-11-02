using System.Text.Json;

namespace TradeSimulator.Commons.Messaging;

// What Account and Portfolio need from the order events Orders publishes. It is a contract on the wire
// (exchange, routing keys, JSON fields), not a reference to Orders code.
public enum OrderExecutionKind
{
    // Part of the order executed: consume that much of the reservation, keep the rest blocked.
    PartialFill,
    // The last execution: consume it and give back whatever is still blocked (e.g. a better price).
    FinalFill,
    // Cancelled, rejected or schedule cancelled: give back everything still blocked.
    Release
}

public sealed record OrderExecutionEvent(Guid EventId, Guid OrderId, OrderExecutionKind Kind, long Quantity, decimal Price)
{
    public const string ExchangeName = "trade-simulator.orders.events";

    public static readonly IReadOnlyList<string> RoutingKeys =
        ["OrderPartiallyFilled", "OrderFilled", "OrderCancelled", "OrderRejected", "OrderScheduleCancelled"];

    // Null when the body is not one of the events above. The order id is a plain GUID or { "guid": "..." }.
    public static OrderExecutionEvent? TryParse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var payload = root.GetProperty("event");
            var kind = root.GetProperty("eventType").GetString() switch
            {
                "OrderPartiallyFilled" => OrderExecutionKind.PartialFill,
                "OrderFilled" => OrderExecutionKind.FinalFill,
                "OrderCancelled" or "OrderRejected" or "OrderScheduleCancelled" => OrderExecutionKind.Release,
                _ => (OrderExecutionKind?)null
            };
            if (kind is null)
                return null;

            var orderId = payload.GetProperty("orderId");
            if (orderId.ValueKind == JsonValueKind.Object)
                orderId = orderId.GetProperty("guid");

            var isFill = kind != OrderExecutionKind.Release;
            return new OrderExecutionEvent(
                root.GetProperty("id").GetGuid(),
                orderId.GetGuid(),
                kind.Value,
                isFill ? payload.GetProperty("filledQuantity").GetInt64() : 0,
                isFill ? payload.GetProperty("executionPrice").GetDecimal() : 0);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
