using TradeSimulator.Account.Domain;

namespace TradeSimulator.Account.Common;

// Outbound port for business metrics. Arguments are closed sets (operation, result); never account or reservation ids.
public interface IAccountMetrics
{
    void OperationCompleted(string operation, decimal? amount, IAccountError? error);
}
