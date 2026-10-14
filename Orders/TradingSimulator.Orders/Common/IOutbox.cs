namespace TradingSimulator.Orders.Common;

public interface IOutbox
{
    Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}
