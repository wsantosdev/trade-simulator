using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Common;

// Outbound port for business metrics. Arguments are closed sets (operation, result, kind); never account/order ids.
public interface IAccountMetrics
{
    void OperationCompleted(string operation, decimal? amount, IAccountError? error);
    void ReservationCompleted(string result, TimeSpan duration);
    void SettlementCompleted(string type, string result);
    void SettlementAbandoned(string type);
}
