namespace TradingSimulator.Portfolio.Application;

// Disposing releases the lock (only if this lease still owns it); an expired lease frees itself.
public interface IEnvelopeLease : IAsyncDisposable
{
    Task<bool> WasProcessedAsync(CancellationToken cancellationToken);

    // Must be called only after the outcome (state change or business failure) is durable.
    Task MarkProcessedAsync(CancellationToken cancellationToken);
}
