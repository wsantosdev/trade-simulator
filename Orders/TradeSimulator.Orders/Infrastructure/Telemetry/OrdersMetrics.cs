using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;

namespace TradeSimulator.Orders.Infrastructure.Telemetry;

public sealed partial class OrdersMetrics : IOrdersMetrics
{
    private static readonly Meter Meter = OrdersTelemetry.Service.Meter;

    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "orders.operations", description: "Order operations (submit, cancel, replace), by result.");

    private static readonly Counter<long> Submitted = Meter.CreateCounter<long>(
        "orders.submitted", description: "Orders accepted, by side and type.");

    private static readonly Counter<long> ExchangeEvents = Meter.CreateCounter<long>(
        "orders.exchange.events", description: "Exchange events applied to orders, by event type.");

    private static readonly Counter<long> Confirmations = Meter.CreateCounter<long>(
        "orders.submission.confirmed", description: "Pre-submission reservations confirmed, by source (funds, assets).");

    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        "orders.submission.rejected", description: "Submissions rejected by a reservation failure, by source and reason.");

    public void OperationCompleted(string operation, IOrderError? error) =>
        Operations.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", error is null ? "succeeded" : error.GetType().Name));

    public void OrderSubmitted(string side, string type) =>
        Submitted.Add(1, new KeyValuePair<string, object?>("side", side), new KeyValuePair<string, object?>("type", type));

    public void ExchangeEventProcessed(string eventType) =>
        ExchangeEvents.Add(1, new KeyValuePair<string, object?>("type", eventType));

    public void SubmissionConfirmed(string source) =>
        Confirmations.Add(1, new KeyValuePair<string, object?>("source", source));

    // The reason comes from another service: only short alphabetic codes are kept as label values.
    public void SubmissionRejected(string source, string? reason) =>
        Rejections.Add(1,
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("reason", reason is not null && ReasonCode().IsMatch(reason) ? reason : "other"));

    [GeneratedRegex("^[A-Za-z]{1,40}$")]
    private static partial Regex ReasonCode();
}
