using TradeSimulator.OfferBook.Domain.Enums;

namespace TradeSimulator.OfferBook.Processors.OfferBookUpdates;

// Outbound port for business metrics. Arguments are closed sets; never tickers or prices.
public interface IOfferBookMetrics
{
    void UpdateProcessed(ProcessOutcome outcome);
    void UpdateMalformed();
}
