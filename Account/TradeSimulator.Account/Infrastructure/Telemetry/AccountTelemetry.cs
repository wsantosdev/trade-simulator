using System.Diagnostics.Metrics;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Account.Infrastructure.Telemetry;

public static class AccountTelemetry
{
    public const string Name = "TradeSimulator.Account";

    public static readonly ServiceTelemetry Service = new(Name, "account");
    private static readonly Meter Meter = Service.Meter;

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
}
