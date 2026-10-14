using System.Text.Json;
using TradingSimulator.Orders.Domain.Events;
using TradingSimulator.Orders.Infrastructure.Serialization;

namespace TradingSimulator.Orders.Infrastructure.Database;

public static class OrderEventSerializer
{
    private static readonly Dictionary<string, Type> Types = new[]
    {
        typeof(OrderSubmitted), typeof(OrderDispatched), typeof(OrderAcked), typeof(OrderRejected), typeof(OrderPartiallyFilled),
        typeof(OrderFilled), typeof(OrderCancelRequested), typeof(OrderCancelled), typeof(OrderCancelRejected),
        typeof(OrderReplaceRequested), typeof(OrderReplaced), typeof(OrderReplaceRejected)
    }.ToDictionary(type => type.Name);

    public const string LegacyOpeningType = "OrderSent";

    // Only these events are commands for the Exchange and go through the outbox.
    public static bool IsPublished(OrderEvent @event) =>
        @event is OrderSubmitted or OrderDispatched or OrderCancelRequested or OrderReplaceRequested;

    public static string Serialize(OrderEvent @event) =>
        JsonSerializer.Serialize(@event, @event.GetType(), OrderJson.Options);

    public static string SerializeEnvelope(OrderEvent @event) =>
        JsonSerializer.Serialize(new EventEnvelope<object>(Guid.CreateVersion7(), @event.GetType().Name, @event), OrderJson.Options);

    public static OrderEvent? Deserialize(string eventType, string payload) =>
        Types.TryGetValue(eventType == LegacyOpeningType ? nameof(OrderSubmitted) : eventType, out var type)
            ? JsonSerializer.Deserialize(payload, type, OrderJson.Options) as OrderEvent
            : null;
}
