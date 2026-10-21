using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;

namespace TradeSimulator.Orders.Tests;

internal sealed class FakeMetrics : IOrdersMetrics
{
    public void OperationCompleted(string operation, IOrderError? error) { }
    public void OrderSubmitted(string side, string type) { }
    public void ExchangeEventProcessed(string eventType) { }
    public void SubmissionConfirmed(string source) { }
    public void SubmissionRejected(string source, string? reason) { }
}
