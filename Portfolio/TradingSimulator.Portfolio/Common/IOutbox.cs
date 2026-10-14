namespace TradingSimulator.Portfolio.Common;

public interface IOutbox
{
    Task<IReadOnlyList<PendingResponse>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}
