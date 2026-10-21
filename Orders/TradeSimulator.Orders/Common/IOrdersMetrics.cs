using TradeSimulator.Orders.Domain;

namespace TradeSimulator.Orders.Common;

// Outbound port for business metrics. Arguments are closed sets; never account/order ids, tickers, prices or quantities.
public interface IOrdersMetrics
{
    void OperationCompleted(string operation, IOrderError? error);
    void OrderSubmitted(string side, string type);
    void ExchangeEventProcessed(string eventType);
    void SubmissionConfirmed(string source);
    void SubmissionRejected(string source, string? reason);
}
