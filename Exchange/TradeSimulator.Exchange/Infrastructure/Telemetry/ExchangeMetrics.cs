using System.Diagnostics.Metrics;
using TradeSimulator.Exchange.Processors.OrderCommands;

namespace TradeSimulator.Exchange.Infrastructure.Telemetry;

public sealed class ExchangeMetrics : IExchangeMetrics
{
    private static readonly Meter Meter = ExchangeTelemetry.Service.Meter;
    private static int _pendingEvents;

    private static readonly Counter<long> Commands = Meter.CreateCounter<long>(
        "exchange.order.commands", description: "Order commands handled by the match engine, by command.");

    private static readonly Counter<long> Events = Meter.CreateCounter<long>(
        "exchange.order.events", description: "Order events produced for the Orders module, by event type.");

    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        "exchange.order.events.dropped", description: "Order events lost because the in-memory backlog was full.");

    private static readonly ObservableGauge<int> Pending = Meter.CreateObservableGauge(
        "exchange.order_events.pending", () => Volatile.Read(ref _pendingEvents),
        description: "Order events waiting for publication (the exchange has no durable outbox).");

    public void CommandHandled(string command) => Commands.Add(1, new KeyValuePair<string, object?>("command", command));
    public void EventQueued(string eventType) => Events.Add(1, new KeyValuePair<string, object?>("type", eventType));
    public void EventDropped() => Dropped.Add(1);
    public void EventBacklog(int pending) => Volatile.Write(ref _pendingEvents, pending);
}
