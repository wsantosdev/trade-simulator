namespace TradingSimulator.Orders.Common;

public interface ICommandPublisher
{
    Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken);
}
