using System.Diagnostics.Metrics;
using TradeSimulator.Account.Common;
using TradeSimulator.Account.Domain;

namespace TradeSimulator.Account.Infrastructure.Telemetry;

public sealed class AccountMetrics : IAccountMetrics
{
    private static readonly Meter Meter = AccountTelemetry.Service.Meter;

    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "account.operations", description: "Account operations (open, deposit, withdraw, reserve, debit, release), by result.");

    private static readonly Histogram<double> OperationAmounts = Meter.CreateHistogram<double>(
        "account.operations.amount", description: "Monetary amount of successful operations, by operation.");

    public void OperationCompleted(string operation, decimal? amount, IAccountError? error)
    {
        Operations.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", error is null ? "succeeded" : error.GetType().Name));
        if (error is null && amount is { } value)
            OperationAmounts.Record((double)value, new KeyValuePair<string, object?>("operation", operation));
    }
}
