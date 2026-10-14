namespace TradingSimulator.Orders.Application;

public interface ICommandPublisher
{
    Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken);
}
