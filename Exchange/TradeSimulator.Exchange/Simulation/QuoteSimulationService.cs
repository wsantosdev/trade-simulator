using TradeSimulator.Commons.Messaging;
using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Processors.OrderCommands;

namespace TradeSimulator.Exchange.Simulation;

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
                    // A cycle that started is finished even if a stop arrives meanwhile, so state and events stay consistent.
                    using var grace = new CancellationTokenSource(OutboxPublishing.ItemTimeout);
                    await simulator.RunCycleAsync(now, grace.Token);
                    await engine.RunCycleAsync(now, grace.Token);
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
