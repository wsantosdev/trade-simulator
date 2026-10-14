using System.Diagnostics.Metrics;
using TradingSimulator.Commons.Telemetry;

namespace TradingSimulator.Quotes.Infrastructure.Telemetry;

public static class QuotesTelemetry
{
    public const string Name = "TradingSimulator.Quotes";

    public static readonly ServiceTelemetry Service = new(Name, "quotes");

    private static readonly Counter<long> PushFailures = Service.Meter.CreateCounter<long>(
        "quotes.push.failures", description: "Real-time quote pushes to clients that failed.");

    public static void PushFailed() => PushFailures.Add(1);
}
