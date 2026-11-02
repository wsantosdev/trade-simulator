namespace TradeSimulator.Outbox;

// A row of a transactional outbox; Position is the key used to mark it as published.
public interface IOutboxItem
{
    long Position { get; }
}

// Implemented by the service store that owns the outbox table.
public interface IOutboxStore<TItem> where TItem : IOutboxItem
{
    Task<IReadOnlyList<TItem>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}

// Implemented by the broker adapter that delivers an item.
public interface IOutboxPublisher<TItem> where TItem : IOutboxItem
{
    Task PublishAsync(TItem item, CancellationToken cancellationToken);
}
