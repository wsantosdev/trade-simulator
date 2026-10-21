using TradeSimulator.Exchange.Domain.Events;

namespace TradeSimulator.Exchange.Processors.OrderCommands;

public interface IOrderEventPublisher
{
    // The id is fixed when the event is queued, so a retried publication carries the same id and the consumer can deduplicate it.
    Task PublishAsync(Guid eventId, OrderEvent orderEvent, CancellationToken cancellationToken);
}
