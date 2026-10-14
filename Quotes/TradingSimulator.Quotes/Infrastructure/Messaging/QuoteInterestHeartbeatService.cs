using TradingSimulator.Quotes.Common;

namespace TradingSimulator.Quotes.Infrastructure.Messaging;

public sealed class QuoteInterestHeartbeatService(
    QuoteInterestHeartbeat heartbeat,
    ILogger<QuoteInterestHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await heartbeat.PublishAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Could not renew the quote interest; retrying.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
