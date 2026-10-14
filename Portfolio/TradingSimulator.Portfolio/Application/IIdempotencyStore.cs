namespace TradingSimulator.Portfolio.Application;

public interface IIdempotencyStore
{
    // Takes an exclusive, expiring lease on the envelope id; null when another worker holds it right now.
    Task<IEnvelopeLease?> TryLockAsync(Guid envelopeId, CancellationToken cancellationToken);
}
