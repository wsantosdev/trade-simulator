using System.Diagnostics.Metrics;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Quotes.Infrastructure.Telemetry;

public static class QuotesTelemetry
{
    public const string Name = "TradeSimulator.Quotes";

    public static readonly ServiceTelemetry Service = new(Name, "quotes");

    private static readonly Counter<long> PushFailures = Service.Meter.CreateCounter<long>(
        "quotes.push.failures", description: "Real-time quote pushes to clients that failed.");

    public static void PushFailed() => PushFailures.Add(1);
}
