namespace TradingSimulator.Account.Application;

public interface IEventPublisher
{
    Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken);
}
