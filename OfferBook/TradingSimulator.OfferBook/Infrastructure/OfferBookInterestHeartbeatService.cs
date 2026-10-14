using TradingSimulator.OfferBook.Application;

namespace TradingSimulator.OfferBook.Infrastructure;

public sealed class OfferBookInterestHeartbeatService(
    OfferBookInterestHeartbeat heartbeat,
    ILogger<OfferBookInterestHeartbeatService> logger) : BackgroundService
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
                    logger.LogWarning(exception, "Could not renew the offer book interest; retrying.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
