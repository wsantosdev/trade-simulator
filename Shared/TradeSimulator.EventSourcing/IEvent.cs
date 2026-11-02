namespace TradeSimulator.EventSourcing;

// Marker contract for every persisted domain event.
public interface IEvent
{
    Guid StreamId { get; }
    DateTimeOffset OccurredAt { get; }
}
