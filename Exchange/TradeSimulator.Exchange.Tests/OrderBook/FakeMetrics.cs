using TradeSimulator.Exchange.Processors.OrderCommands;

namespace TradeSimulator.Exchange.Tests.OrderBook;

internal sealed class FakeMetrics : IExchangeMetrics
{
    public void CommandHandled(string command) { }
    public void EventQueued(string eventType) { }
    public void EventDropped() { }
    public void EventBacklog(int pending) { }
}
