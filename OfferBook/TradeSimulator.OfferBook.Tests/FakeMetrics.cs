using TradeSimulator.OfferBook.Domain.Enums;

using TradeSimulator.OfferBook.Processors.OfferBookUpdates;

namespace TradeSimulator.OfferBook.Tests;

internal sealed class FakeMetrics : IOfferBookMetrics
{
    public void UpdateProcessed(ProcessOutcome outcome) { }
    public void UpdateMalformed() { }
}
