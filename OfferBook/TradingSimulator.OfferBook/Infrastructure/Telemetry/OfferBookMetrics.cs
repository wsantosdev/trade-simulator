using TradingSimulator.OfferBook.Domain.Enums;

using TradingSimulator.OfferBook.Processors.OfferBookUpdates;

namespace TradingSimulator.OfferBook.Infrastructure.Telemetry;

public sealed class OfferBookMetrics : IOfferBookMetrics
{
    public void UpdateProcessed(ProcessOutcome outcome) => OfferBookTelemetry.Update(outcome.ToString());
    public void UpdateMalformed() => OfferBookTelemetry.Update("Malformed");
}
