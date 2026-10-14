using TradingSimulator.Exchange.Application;

namespace TradingSimulator.Exchange.Infrastructure;

public sealed class QuoteSimulationService(
    QuoteSimulator simulator,
    OrderBookEngine engine,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<QuoteSimulationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Test-only: shifts the session clock (e.g. 09:00:00 turns 01h into 10h) to exercise the open session off-hours.
        var clockOffset = configuration.GetValue<TimeSpan>("Simulation:SessionClockOffset");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var now = timeProvider.GetUtcNow() + clockOffset;
                    await simulator.RunCycleAsync(now, stoppingToken);
                    await engine.RunCycleAsync(now, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Quote simulation cycle failed; retrying on the next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
