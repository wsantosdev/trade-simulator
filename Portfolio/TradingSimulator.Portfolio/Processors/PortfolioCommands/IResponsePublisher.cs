namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

public interface IResponsePublisher
{
    // Failures are published directly; successes travel through the store's outbox.
    Task PublishFailureAsync(PortfolioCommandError failure, CancellationToken cancellationToken);

    Task PublishPendingAsync(PendingResponse pending, CancellationToken cancellationToken);
}
