namespace TradeSimulator.Orders.Infrastructure.Messaging;

public interface ICommandPublisher
{
    Task PublishAsync(PendingEvent pendingEvent, CancellationToken cancellationToken);
}
