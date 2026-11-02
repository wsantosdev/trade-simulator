namespace TradeSimulator.EventSourcing;

// Base for event-sourced entities: tracks the uncommitted events and the version (events applied).
public abstract class EventSourcedEntity
{
    private readonly List<IEvent> _uncommittedEvents = [];

    public int Version { get; protected set; }
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

    public void CommitEvents() => _uncommittedEvents.Clear();

    protected abstract void ProcessEvent(IEvent @event);

    // Applies a new event and queues it for persistence.
    protected void RaiseEvent(IEvent @event)
    {
        ProcessEvent(@event);
        Version++;
        _uncommittedEvents.Add(@event);
    }

    // Applies persisted events while rehydrating.
    protected void FeedEvents(IEnumerable<IEvent> stream)
    {
        foreach (var @event in stream)
            ReplayEvent(@event);
    }

    protected void ReplayEvent(IEvent @event)
    {
        ProcessEvent(@event);
        Version++;
    }

    // For entities whose apply step is fallible and already ran: queue it and/or count it.
    protected void Advance() => Version++;

    protected void Record(IEvent @event) => _uncommittedEvents.Add(@event);
}
