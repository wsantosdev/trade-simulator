using TradingSimulator.Exchange.Processors.OrderCommands;

namespace TradingSimulator.Exchange.Infrastructure.Telemetry;

public sealed class ExchangeMetrics : IExchangeMetrics
{
    public void CommandHandled(string command) => ExchangeTelemetry.Command(command);
    public void EventQueued(string eventType) => ExchangeTelemetry.Event(eventType);
    public void EventDropped() => ExchangeTelemetry.EventDropped();
    public void EventBacklog(int pending) => ExchangeTelemetry.Backlog(pending);
}
