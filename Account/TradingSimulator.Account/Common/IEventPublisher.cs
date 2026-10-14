namespace TradingSimulator.Account.Common;

public interface IEventPublisher
{
    Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken);
}
