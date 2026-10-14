using System.Text.RegularExpressions;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Infrastructure.Telemetry;

public sealed partial class OrdersMetrics : IOrdersMetrics
{
    public void OperationCompleted(string operation, IOrderError? error) =>
        OrdersTelemetry.Operation(operation, error is null ? "succeeded" : error.GetType().Name);

    public void OrderSubmitted(string side, string type) => OrdersTelemetry.OrderSubmitted(side, type);

    public void ExchangeEventProcessed(string eventType) => OrdersTelemetry.ExchangeEvent(eventType);

    public void SubmissionConfirmed(string source) => OrdersTelemetry.Confirmed(source);

    // The reason comes from another service: only short alphabetic codes are kept as label values.
    public void SubmissionRejected(string source, string? reason) =>
        OrdersTelemetry.Rejected(source, reason is not null && ReasonCode().IsMatch(reason) ? reason : "other");

    [GeneratedRegex("^[A-Za-z]{1,40}$")]
    private static partial Regex ReasonCode();
}
