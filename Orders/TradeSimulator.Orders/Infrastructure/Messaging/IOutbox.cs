namespace TradeSimulator.Orders.Infrastructure.Messaging;

public interface IOutbox
{
    Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}
