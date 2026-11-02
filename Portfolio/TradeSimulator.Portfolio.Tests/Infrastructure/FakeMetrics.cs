using TradeSimulator.Portfolio.Common;

namespace TradeSimulator.Portfolio.Tests.Infrastructure;

internal sealed class FakeMetrics : IPortfolioMetrics
{
    public void CommandApplied(string operation) { }
    public void CommandRejected(string error) { }
    public void ReservationBlocked() { }
    public void ReservationRejected(string kind) { }
    public void ReservationSettled(string eventName) { }
}
