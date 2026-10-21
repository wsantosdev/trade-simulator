using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace TradeSimulator.Commons.Messaging;

// Lets a consumer finish the messages it already received when the service is asked to stop.
// Handlers run with Token, which is only cancelled if draining exceeds the timeout.
public sealed class ConsumerDrain : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _timeout;
    private int _inFlight;

    public ConsumerDrain(TimeSpan? timeout = null) => _timeout = timeout ?? DefaultTimeout;

    public CancellationToken Token => _cts.Token;

    public bool TimedOut => _cts.IsCancellationRequested;

    // Wrap the whole handler in `using var _ = drain.Begin();`.
    public IDisposable Begin()
    {
        Interlocked.Increment(ref _inFlight);
        return new Scope(this);
    }

    public async Task StopAsync(IChannel channel, string consumerTag, string queue, ILogger logger)
    {
        _cts.CancelAfter(_timeout);
        try
        {
            await channel.BasicCancelAsync(consumerTag, noWait: false, _cts.Token);
            logger.LogInformation(
                "Stopping {Queue}: waiting for {Count} message(s) in progress.", queue, Volatile.Read(ref _inFlight));
            while (Volatile.Read(ref _inFlight) > 0)
                await Task.Delay(50, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Stopping {Queue}: gave up waiting; unfinished messages will be redelivered.", queue);
        }
    }

    public void Dispose() => _cts.Dispose();

    private sealed class Scope(ConsumerDrain owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._inFlight);
    }
}
