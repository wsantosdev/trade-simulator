using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain;

namespace TradingSimulator.Account.Infrastructure.Telemetry;

public sealed class AccountMetrics : IAccountMetrics
{
    public void OperationCompleted(string operation, decimal? amount, IAccountError? error)
    {
        AccountTelemetry.Operation(operation, error is null ? "succeeded" : error.GetType().Name);
        if (error is null && amount is { } value)
            AccountTelemetry.OperationAmount(operation, value);
    }

    public void ReservationCompleted(string result, TimeSpan duration) =>
        AccountTelemetry.Reservation(result, duration);

    public void SettlementCompleted(string type, string result) =>
        AccountTelemetry.Settlement(type, result);

    public void SettlementAbandoned(string type) =>
        AccountTelemetry.SettlementAbandoned(type);
}
