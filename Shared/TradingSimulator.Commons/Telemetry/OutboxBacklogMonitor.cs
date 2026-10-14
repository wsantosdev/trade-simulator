using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingSimulator.Commons.Telemetry;

// Polls the backlog on a timer so metric collection never touches the database.
public sealed class OutboxBacklogMonitor(
    ServiceTelemetry telemetry,
    IOutboxBacklogReader reader,
    ILogger<OutboxBacklogMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                telemetry.UpdateBacklog(await reader.ReadOutboxBacklogAsync(stoppingToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Outbox backlog could not be read.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
