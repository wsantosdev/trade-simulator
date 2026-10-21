using System.Diagnostics.Metrics;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Exchange.Infrastructure.Telemetry;

public static class ExchangeTelemetry
{
    public const string Name = "TradeSimulator.Exchange";

    public static readonly ServiceTelemetry Service = new(Name, "exchange");

    private static int _pendingEvents;

    private static readonly Counter<long> Commands = Service.Meter.CreateCounter<long>(
        "exchange.order.commands", description: "Order commands handled by the match engine, by command.");

    private static readonly Counter<long> Events = Service.Meter.CreateCounter<long>(
        "exchange.order.events", description: "Order events produced for the Orders module, by event type.");

    private static readonly Counter<long> Dropped = Service.Meter.CreateCounter<long>(
        "exchange.order.events.dropped", description: "Order events lost because the in-memory backlog was full.");

    private static readonly ObservableGauge<int> Pending = Service.Meter.CreateObservableGauge(
        "exchange.order_events.pending", () => Volatile.Read(ref _pendingEvents),
        description: "Order events waiting for publication (the exchange has no durable outbox).");

    public static void Command(string command) => Commands.Add(1, new KeyValuePair<string, object?>("command", command));

    public static void Event(string eventType) => Events.Add(1, new KeyValuePair<string, object?>("type", eventType));

    public static void EventDropped() => Dropped.Add(1);

    public static void Backlog(int pending) => Volatile.Write(ref _pendingEvents, pending);
}
