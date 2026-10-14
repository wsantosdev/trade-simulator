using TradingSimulator.Account.Infrastructure.Database;

namespace TradingSimulator.Account.Infrastructure.Telemetry;

// Polls the backlog on a timer so metric collection never touches the database.
public sealed class OutboxBacklogMonitor(PostgresAccountStore store, ILogger<OutboxBacklogMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                AccountTelemetry.UpdateBacklog(await store.ReadOutboxBacklogAsync(stoppingToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Outbox backlog could not be read.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
