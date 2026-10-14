namespace TradingSimulator.Portfolio.Common;

// Outbound port for business metrics. Arguments are closed sets; never account/order ids, tickers or quantities.
public interface IPortfolioMetrics
{
    void CommandApplied(string operation);
    void CommandRejected(string error);
    void ReservationBlocked();
    void ReservationRejected(string kind);
    void OrderSettled(string eventName);
    void SettlementDiscarded();
    void DuplicateEnvelope();
}
