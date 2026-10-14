using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TradingSimulator.Account.Infrastructure.Telemetry;

public static class AccountTelemetry
{
    public const string Name = "TradingSimulator.Account";
    public const string TraceParentHeader = "traceparent";

    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);

    private static readonly Counter<long> Consumed = Meter.CreateCounter<long>(
        "account.messages.consumed", description: "Messages consumed, by queue and result (acked, requeued).");

    private static readonly Counter<long> Published = Meter.CreateCounter<long>(
        "account.outbox.published", description: "Outbox entries published to the broker, by exchange.");

    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "account.operations", description: "Account operations (open, deposit, withdraw, reserve), by result.");

    private static readonly Histogram<double> OperationAmounts = Meter.CreateHistogram<double>(
        "account.operations.amount", description: "Monetary amount of successful operations, by operation.");

    private static readonly Counter<long> Reservations = Meter.CreateCounter<long>(
        "account.funds.reservations", description: "Order fund reservations, by result.");

    private static readonly Histogram<double> ReservationDurations = Meter.CreateHistogram<double>(
        "account.funds.reservation.duration", unit: "s", description: "Time to process an OrderSubmitted until the outcome is persisted.");

    private static readonly Counter<long> Settlements = Meter.CreateCounter<long>(
        "account.funds.settlements", description: "Settlements of fills and closures, by type and result.");

    private static readonly Counter<long> SettlementsAbandoned = Meter.CreateCounter<long>(
        "account.funds.settlements.abandoned", description: "Settlements discarded after exhausting retries; alert on any value above zero.");

    private static readonly Histogram<double> GateWaits = Meter.CreateHistogram<double>(
        "account.store.gate.wait", unit: "s", description: "Time spent waiting for the account write gate (contention).");

    private static volatile IReadOnlyList<OutboxBacklog> _backlog = [];

    private static readonly ObservableGauge<long> BacklogPending = Meter.CreateObservableGauge(
        "account.outbox.pending", () => _backlog.Select(b => new Measurement<long>(b.Pending, new KeyValuePair<string, object?>("outbox", b.Outbox))),
        description: "Outbox entries waiting to be published.");

    private static readonly ObservableGauge<double> BacklogOldestAge = Meter.CreateObservableGauge(
        "account.outbox.oldest_age", () => _backlog.Select(b => new Measurement<double>(b.OldestAgeSeconds, new KeyValuePair<string, object?>("outbox", b.Outbox))),
        unit: "s", description: "Age of the oldest unpublished outbox entry.");

    public static void UpdateBacklog(IReadOnlyList<OutboxBacklog> backlog) => _backlog = backlog;

    public static void Operation(string operation, string result) =>
        Operations.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("result", result));

    public static void OperationAmount(string operation, decimal amount) =>
        OperationAmounts.Record((double)amount, new KeyValuePair<string, object?>("operation", operation));

    public static void Reservation(string result, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("result", result);
        Reservations.Add(1, tag);
        ReservationDurations.Record(duration.TotalSeconds, tag);
    }

    public static void Settlement(string type, string result) =>
        Settlements.Add(1, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("result", result));

    public static void SettlementAbandoned(string type) =>
        SettlementsAbandoned.Add(1, new KeyValuePair<string, object?>("type", type));

    public static void GateWait(TimeSpan duration) => GateWaits.Record(duration.TotalSeconds);

    public static void MessageConsumed(string queue, string result) =>
        Consumed.Add(1, new KeyValuePair<string, object?>("queue", queue), new KeyValuePair<string, object?>("result", result));

    public static void OutboxPublished(string exchange) =>
        Published.Add(1, new KeyValuePair<string, object?>("exchange", exchange));

    // The outbox decouples the write from the publication, so the trace context travels with the row.
    public static string? CurrentTraceParent() => Activity.Current?.Id;

    public static Activity? StartConsumer(string queue, string? traceParent)
    {
        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, ParentOf(traceParent));
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.operation", "process");
        return activity;
    }

    public static Activity? StartPublisher(string exchange, string routingKey, string? traceParent)
    {
        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer, ParentOf(traceParent));
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.operation", "publish");
        return activity;
    }

    public static void Fail(this Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);
    }

    private static ActivityContext ParentOf(string? traceParent) =>
        ActivityContext.TryParse(traceParent, null, out var context) ? context : default;
}
