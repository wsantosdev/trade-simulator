using System.Diagnostics.Metrics;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.OfferBook.Infrastructure.Telemetry;

public static class OfferBookTelemetry
{
    public const string Name = "TradeSimulator.OfferBook";

    public static readonly ServiceTelemetry Service = new(Name, "offerbook");

    private static readonly Counter<long> Updates = Service.Meter.CreateCounter<long>(
        "offerbook.updates", description: "Offer book messages processed, by outcome (snapshot stored, delta forwarded, snapshot requested, ignored, malformed).");

    private static readonly Counter<long> PushFailures = Service.Meter.CreateCounter<long>(
        "offerbook.push.failures", description: "Real-time pushes to clients that failed, by message kind.");

    public static void Update(string outcome) => Updates.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void PushFailed(string method) => PushFailures.Add(1, new KeyValuePair<string, object?>("kind", method));
}
