using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSimulator.Account.Application;

namespace TradingSimulator.Account.Infrastructure;

public sealed class OutboxPublisherService(IOutbox outbox, IEventPublisher publisher, ILogger<OutboxPublisherService> logger)
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
                    await publisher.PublishAsync(item, stoppingToken);
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
                logger.LogError(exception, "Failed to publish account events from the outbox.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
