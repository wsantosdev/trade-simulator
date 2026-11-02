namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public interface IResponsePublisher
{
    // Failures are published directly; successes travel through the store's outbox (see IOutboxPublisher<PendingResponse>).
    Task PublishFailureAsync(PortfolioCommandError failure, CancellationToken cancellationToken);
}
