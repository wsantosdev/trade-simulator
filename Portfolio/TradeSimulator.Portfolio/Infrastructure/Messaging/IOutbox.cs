using TradeSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public interface IOutbox
{
    Task<IReadOnlyList<PendingResponse>> ReadPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(long position, CancellationToken cancellationToken);
}
