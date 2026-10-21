using System.Diagnostics.Metrics;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Portfolio.Infrastructure.Telemetry;

public static class PortfolioTelemetry
{
    public const string Name = "TradeSimulator.Portfolio";

    public static readonly ServiceTelemetry Service = new(Name, "portfolio");

    private static readonly Counter<long> Commands = Service.Meter.CreateCounter<long>(
        "portfolio.commands", description: "Asset commands, by operation or error and result.");

    private static readonly Counter<long> Reservations = Service.Meter.CreateCounter<long>(
        "portfolio.reservations", description: "Asset reservations for sell orders, by result.");

    private static readonly Counter<long> Settlements = Service.Meter.CreateCounter<long>(
        "portfolio.settlements", description: "Order fills and closures settled, by event and result.");

    private static readonly Counter<long> Duplicates = Service.Meter.CreateCounter<long>(
        "portfolio.envelopes.duplicated", description: "Envelopes skipped because they were already applied.");

    public static void CommandApplied(string operation) =>
        Commands.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("result", "applied"));

    public static void CommandRejected(string error) =>
        Commands.Add(1, new KeyValuePair<string, object?>("operation", "any"), new KeyValuePair<string, object?>("result", error));

    public static void Reservation(string result) =>
        Reservations.Add(1, new KeyValuePair<string, object?>("result", result));

    public static void Settlement(string eventName, string result) =>
        Settlements.Add(1, new KeyValuePair<string, object?>("event", eventName), new KeyValuePair<string, object?>("result", result));

    public static void Duplicate() => Duplicates.Add(1);
}
