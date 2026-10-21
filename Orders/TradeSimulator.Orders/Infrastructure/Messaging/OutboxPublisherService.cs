using Microsoft.Extensions.Hosting;
using TradeSimulator.Commons.Messaging;
using Microsoft.Extensions.Logging;

namespace TradeSimulator.Orders.Infrastructure.Messaging;

public sealed class OutboxPublisherService(IOutbox outbox, ICommandPublisher publisher, ILogger<OutboxPublisherService> logger)
    : BackgroundService
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
                    if (stoppingToken.IsCancellationRequested)
                        break;
                    // The item in progress is finished (publish and mark) even if a stop arrives meanwhile.
                    using var grace = new CancellationTokenSource(OutboxPublishing.ItemTimeout);
                    await publisher.PublishAsync(item, grace.Token);
                    await outbox.MarkPublishedAsync(item.Position, grace.Token);
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
                logger.LogError(exception, "Failed to publish order commands from the outbox.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
