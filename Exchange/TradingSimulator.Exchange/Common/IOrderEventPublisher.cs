namespace TradingSimulator.Exchange.Common;

public interface IOrderEventPublisher
{
    Task PublishAsync(EventEnvelope<object> envelope, CancellationToken cancellationToken);
}
