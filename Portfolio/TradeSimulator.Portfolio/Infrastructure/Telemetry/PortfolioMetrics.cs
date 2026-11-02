using System.Diagnostics.Metrics;
using TradeSimulator.Portfolio.Common;

namespace TradeSimulator.Portfolio.Infrastructure.Telemetry;

public sealed class PortfolioMetrics : IPortfolioMetrics
{
    private static readonly Counter<long> Commands = PortfolioTelemetry.Service.Meter.CreateCounter<long>(
        "portfolio.commands", description: "Asset commands, by operation or error and result.");

    private static readonly Counter<long> Reservations = PortfolioTelemetry.Service.Meter.CreateCounter<long>(
        "portfolio.reservations", description: "Asset reservations, by result.");

    private static readonly Counter<long> Settlements = PortfolioTelemetry.Service.Meter.CreateCounter<long>(
        "portfolio.settlements", description: "Reservation settlements and closures, by event and result.");

    public void CommandApplied(string operation) =>
        Commands.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("result", "applied"));

    public void CommandRejected(string error) =>
        Commands.Add(1, new KeyValuePair<string, object?>("operation", "any"), new KeyValuePair<string, object?>("result", error));

    public void ReservationBlocked() => Reservations.Add(1, new KeyValuePair<string, object?>("result", "blocked"));

    public void ReservationRejected(string kind) => Reservations.Add(1, new KeyValuePair<string, object?>("result", kind));

    public void ReservationSettled(string eventName) =>
        Settlements.Add(1, new KeyValuePair<string, object?>("event", eventName), new KeyValuePair<string, object?>("result", "settled"));
}
