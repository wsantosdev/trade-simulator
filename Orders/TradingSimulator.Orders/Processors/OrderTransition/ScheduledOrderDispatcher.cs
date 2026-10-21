using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSimulator.Orders.Common;

namespace TradingSimulator.Orders.Processors.OrderTransition;

// Dispatches the orders scheduled while the market was closed, as soon as it opens.
public sealed class ScheduledOrderDispatcher(
    IMarketHours marketHours, IScheduledOrders scheduled, IOrderTransitions transitions, ILogger<ScheduledOrderDispatcher> logger)
    : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await DispatchDueAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Scheduled order dispatch failed; retrying on the next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task DispatchDueAsync(CancellationToken cancellationToken)
    {
        while (marketHours.IsOpen())
        {
            var ids = await scheduled.ListAsync(BatchSize, cancellationToken);
            if (ids.Count == 0)
                return;

            foreach (var id in ids)
                await transitions.DispatchAsync(id, cancellationToken);
            logger.LogInformation("Dispatched {Count} scheduled orders at the market opening.", ids.Count);
        }
    }
}
