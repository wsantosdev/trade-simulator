using TradingSimulator.Portfolio.Common;

namespace TradingSimulator.Portfolio.Infrastructure.Telemetry;

public sealed class PortfolioMetrics : IPortfolioMetrics
{
    public void CommandApplied(string operation) => PortfolioTelemetry.CommandApplied(operation);
    public void CommandRejected(string error) => PortfolioTelemetry.CommandRejected(error);
    public void ReservationBlocked() => PortfolioTelemetry.Reservation("blocked");
    public void ReservationRejected(string kind) => PortfolioTelemetry.Reservation(kind);
    public void OrderSettled(string eventName) => PortfolioTelemetry.Settlement(eventName, "settled");
    public void SettlementDiscarded() => PortfolioTelemetry.Settlement("unknown", "discarded");
    public void DuplicateEnvelope() => PortfolioTelemetry.Duplicate();
}
