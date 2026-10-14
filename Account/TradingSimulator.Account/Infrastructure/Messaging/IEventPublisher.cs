namespace TradingSimulator.Account.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken);
}
