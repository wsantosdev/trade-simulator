namespace TradingSimulator.Commons;

public sealed record EventEnvelope<T>(Guid Id, string EventType, T Event);

public static class EventEnvelope
{
    public static EventEnvelope<T> Of<T>(T @event) => new(Guid.CreateVersion7(), typeof(T).Name, @event);
}
