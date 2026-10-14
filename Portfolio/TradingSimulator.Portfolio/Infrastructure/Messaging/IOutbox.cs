using TradingSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradingSimulator.Portfolio.Infrastructure.Messaging;

public interface IOutbox
{
    Task<IReadOnlyList<PendingResponse>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}
