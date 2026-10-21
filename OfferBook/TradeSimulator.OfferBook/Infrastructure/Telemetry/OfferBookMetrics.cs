using TradeSimulator.OfferBook.Domain.Enums;

using TradeSimulator.OfferBook.Processors.OfferBookUpdates;

namespace TradeSimulator.OfferBook.Infrastructure.Telemetry;

public sealed class OfferBookMetrics : IOfferBookMetrics
{
    public void UpdateProcessed(ProcessOutcome outcome) => OfferBookTelemetry.Update(outcome.ToString());
    public void UpdateMalformed() => OfferBookTelemetry.Update("Malformed");
}
