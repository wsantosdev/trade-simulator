using System.Diagnostics.Metrics;
using TradeSimulator.OfferBook.Domain.Enums;
using TradeSimulator.OfferBook.Processors.OfferBookUpdates;

namespace TradeSimulator.OfferBook.Infrastructure.Telemetry;

public sealed class OfferBookMetrics : IOfferBookMetrics
{
    private static readonly Counter<long> Updates = OfferBookTelemetry.Service.Meter.CreateCounter<long>(
        "offerbook.updates", description: "Offer book messages processed, by outcome (snapshot stored, delta forwarded, snapshot requested, ignored, malformed).");

    public void UpdateProcessed(ProcessOutcome outcome) => Update(outcome.ToString());
    public void UpdateMalformed() => Update("Malformed");

    private static void Update(string outcome) => Updates.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
