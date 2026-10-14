namespace TradingSimulator.Exchange.Processors.OrderCommands;

// Outbound port for business metrics. Arguments are closed sets; never order ids, accounts, tickers, prices or quantities.
public interface IExchangeMetrics
{
    void CommandHandled(string command);
    void EventQueued(string eventType);
    void EventDropped();
    void EventBacklog(int pending);
}
