using System.Diagnostics.Metrics;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Orders.Infrastructure.Telemetry;

public static class OrdersTelemetry
{
    public const string Name = "TradeSimulator.Orders";

    public static readonly ServiceTelemetry Service = new(Name, "orders");

    private static readonly Counter<long> Operations = Service.Meter.CreateCounter<long>(
        "orders.operations", description: "Order operations (submit, cancel, replace), by result.");

    private static readonly Counter<long> Submitted = Service.Meter.CreateCounter<long>(
        "orders.submitted", description: "Orders accepted, by side and type.");

    private static readonly Counter<long> ExchangeEvents = Service.Meter.CreateCounter<long>(
        "orders.exchange.events", description: "Exchange events applied to orders, by event type.");

    private static readonly Counter<long> Confirmations = Service.Meter.CreateCounter<long>(
        "orders.submission.confirmed", description: "Pre-submission reservations confirmed, by source (funds, assets).");

    private static readonly Counter<long> Rejections = Service.Meter.CreateCounter<long>(
        "orders.submission.rejected", description: "Submissions rejected by a reservation failure, by source and reason.");

    public static void Operation(string operation, string result) =>
        Operations.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("result", result));

    public static void OrderSubmitted(string side, string type) =>
        Submitted.Add(1, new KeyValuePair<string, object?>("side", side), new KeyValuePair<string, object?>("type", type));

    public static void ExchangeEvent(string eventType) =>
        ExchangeEvents.Add(1, new KeyValuePair<string, object?>("type", eventType));

    public static void Confirmed(string source) =>
        Confirmations.Add(1, new KeyValuePair<string, object?>("source", source));

    public static void Rejected(string source, string reason) =>
        Rejections.Add(1, new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("reason", reason));
}
