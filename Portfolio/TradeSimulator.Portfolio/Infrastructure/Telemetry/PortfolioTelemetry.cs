using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Portfolio.Infrastructure.Telemetry;

public static class PortfolioTelemetry
{
    public const string Name = "TradeSimulator.Portfolio";

    public static readonly ServiceTelemetry Service = new(Name, "portfolio");
}
