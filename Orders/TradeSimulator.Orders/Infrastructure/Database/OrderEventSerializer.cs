using System.Text.Json;
using TradeSimulator.EventSourcing;
using TradeSimulator.Orders.Domain.Events;
using TradeSimulator.Orders.Infrastructure.Serialization;

namespace TradeSimulator.Orders.Infrastructure.Database;

public static class OrderEventSerializer
{
    public static EventStore CreateStore() => new(new EventStoreOptions(
        OrderJson.Options, [typeof(OrderEvent).Assembly]));

    // Only these events are commands for the Exchange; all of them go through the outbox.
    public static bool IsCommand(string eventType) =>
        eventType is nameof(OrderSubmitted) or nameof(OrderDispatched) or nameof(OrderCancelRequested)
            or nameof(OrderReplaceRequested) or nameof(OrderScheduleCancelled);

    public static string SerializeEnvelope(OrderEvent @event) =>
        JsonSerializer.Serialize(new EventEnvelope<object>(Guid.CreateVersion7(), @event.GetType().Name, @event), OrderJson.Options);
}
