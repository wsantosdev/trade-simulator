using TradingSimulator.OfferBook.Domain.Enums;

using TradingSimulator.OfferBook.Processors.OfferBookUpdates;

namespace TradingSimulator.OfferBook.Tests;

internal sealed class FakeMetrics : IOfferBookMetrics
{
    public void UpdateProcessed(ProcessOutcome outcome) { }
    public void UpdateMalformed() { }
}
