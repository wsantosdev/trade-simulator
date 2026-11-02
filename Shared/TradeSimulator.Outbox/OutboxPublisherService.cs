using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradeSimulator.Outbox;

public sealed record OutboxPublisherOptions(string Name, TimeSpan IdleDelay);

// Drains an outbox: reads pending items, publishes each one and marks it as published.
public sealed class OutboxPublisherService<TItem>(
    IOutboxStore<TItem> outbox,
    IOutboxPublisher<TItem> publisher,
    OutboxPublisherOptions options,
    ILogger<OutboxPublisherService<TItem>> logger) : BackgroundService where TItem : IOutboxItem
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await outbox.ReadPendingAsync(BatchSize, stoppingToken);
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
                    await Task.Delay(options.IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to publish {Outbox} from the outbox.", options.Name);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
