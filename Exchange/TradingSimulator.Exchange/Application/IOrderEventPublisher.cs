namespace TradingSimulator.Exchange.Application;

public interface IOrderEventPublisher
{
    Task PublishAsync(EventEnvelope<object> envelope, CancellationToken cancellationToken);
}
