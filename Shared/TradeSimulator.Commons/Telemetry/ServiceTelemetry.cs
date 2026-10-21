using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TradeSimulator.Commons.Telemetry;

// Per-service ActivitySource and Meter plus the messaging instruments every service shares.
// Only BCL types: Domain and Processors may depend on the metrics ports without referencing OpenTelemetry.
public sealed class ServiceTelemetry
{
    public const string TraceParentHeader = "traceparent";

    private readonly Counter<long> _consumed;
    private readonly Counter<long> _published;
    private volatile IReadOnlyList<OutboxBacklog> _backlog = [];

    public ServiceTelemetry(string name, string prefix)
    {
        Name = name;
        Source = new ActivitySource(name);
        Meter = new Meter(name);
        _consumed = Meter.CreateCounter<long>(
            $"{prefix}.messages.consumed", description: "Messages consumed, by queue and result (acked, requeued).");
        _published = Meter.CreateCounter<long>(
            $"{prefix}.outbox.published", description: "Outbox entries published to the broker, by exchange.");
        Meter.CreateObservableGauge(
            $"{prefix}.outbox.pending",
            () => _backlog.Select(b => new Measurement<long>(b.Pending, new KeyValuePair<string, object?>("outbox", b.Outbox))),
            description: "Outbox entries waiting to be published.");
        Meter.CreateObservableGauge(
            $"{prefix}.outbox.oldest_age",
            () => _backlog.Select(b => new Measurement<double>(b.OldestAgeSeconds, new KeyValuePair<string, object?>("outbox", b.Outbox))),
            unit: "s", description: "Age of the oldest unpublished outbox entry.");
    }

    public string Name { get; }
    public ActivitySource Source { get; }
    public Meter Meter { get; }

    public void UpdateBacklog(IReadOnlyList<OutboxBacklog> backlog) => _backlog = backlog;

    public void MessageConsumed(string queue, string result) =>
        _consumed.Add(1, new KeyValuePair<string, object?>("queue", queue), new KeyValuePair<string, object?>("result", result));

    public void OutboxPublished(string exchange) =>
        _published.Add(1, new KeyValuePair<string, object?>("exchange", exchange));

    // The outbox decouples the write from the publication, so the trace context travels with the row.
    public static string? CurrentTraceParent() => Activity.Current?.Id;

    public Activity? StartConsumer(string queue, string? traceParent)
    {
        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, ParentOf(traceParent));
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.operation", "process");
        return activity;
    }

    public Activity? StartPublisher(string exchange, string routingKey, string? traceParent)
    {
        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer, ParentOf(traceParent));
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.operation", "publish");
        return activity;
    }

    // Reads the traceparent from AMQP headers, which RabbitMQ.Client delivers as byte[].
    public static string? TraceParentOf(IDictionary<string, object?>? headers) =>
        headers is not null && headers.TryGetValue(TraceParentHeader, out var value) && value is byte[] bytes
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : null;

    private static ActivityContext ParentOf(string? traceParent) =>
        ActivityContext.TryParse(traceParent, null, out var context) ? context : default;
}

public static class ActivityExtensions
{
    public static void Fail(this Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);
    }
}
