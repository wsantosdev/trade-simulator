namespace TradeSimulator.Portfolio.Common;

// Outbound port for business metrics. Arguments are closed sets; never account/reservation ids, tickers or quantities.
public interface IPortfolioMetrics
{
    void CommandApplied(string operation);
    void CommandRejected(string error);
    void ReservationBlocked();
    void ReservationRejected(string kind);
    void ReservationSettled(string eventName);
}
