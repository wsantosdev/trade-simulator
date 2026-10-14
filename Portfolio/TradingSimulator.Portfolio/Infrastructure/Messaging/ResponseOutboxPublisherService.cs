using TradingSimulator.Portfolio.Common;

namespace TradingSimulator.Portfolio.Infrastructure.Messaging;

public sealed class ResponseOutboxPublisherService(
    IOutbox outbox,
    IResponsePublisher publisher,
    ILogger<ResponseOutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await outbox.ReadPendingAsync(50, stoppingToken);
                foreach (var item in pending)
                {
                    await publisher.PublishPendingAsync(item, stoppingToken);
                    await outbox.MarkPublishedAsync(item.Position, stoppingToken);
                }

                if (pending.Count == 0)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to publish portfolio responses from the outbox.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
