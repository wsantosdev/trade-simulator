namespace TradingSimulator.Portfolio.Common;

public interface IResponsePublisher
{
    // Failures are published directly; successes travel through the store's outbox.
    Task PublishFailureAsync(PortfolioResponse response, CancellationToken cancellationToken);

    Task PublishPendingAsync(PendingResponse pending, CancellationToken cancellationToken);
}
