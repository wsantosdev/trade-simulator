using System.Text.Json;

namespace TradeSimulator.Testing;

// Bodies shaped like the order events Orders publishes (envelope + payload, order id as { "guid": ... }).
public static class OrderEventMessages
{
    public const string Exchange = "trade-simulator.orders.events";

    public static string Fill(string eventType, Guid orderId, long quantity, decimal price, Guid? eventId = null) =>
        Envelope(eventType, eventId, new { orderId = new { guid = orderId }, filledQuantity = quantity, executionPrice = price });

    public static string Cancel(string eventType, Guid orderId, Guid? eventId = null) =>
        Envelope(eventType, eventId, new { orderId = new { guid = orderId } });

    private static string Envelope(string eventType, Guid? eventId, object payload) =>
        JsonSerializer.Serialize(new { id = eventId ?? Guid.CreateVersion7(), eventType, @event = payload });
}
