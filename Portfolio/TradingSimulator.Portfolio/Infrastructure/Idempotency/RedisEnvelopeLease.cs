using StackExchange.Redis;
using TradingSimulator.Portfolio.Common;

namespace TradingSimulator.Portfolio.Infrastructure.Idempotency;

public sealed class RedisEnvelopeLease(IDatabase database, Guid envelopeId, string token, ILogger logger) : IEnvelopeLease
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<bool> WasProcessedAsync(CancellationToken cancellationToken) =>
        await database.KeyExistsAsync(RedisIdempotencyStore.KeyOf(envelopeId));

    public Task MarkProcessedAsync(CancellationToken cancellationToken) =>
        database.StringSetAsync(RedisIdempotencyStore.KeyOf(envelopeId), 1, RedisIdempotencyStore.Ttl);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await database.ScriptEvaluateAsync(
                ReleaseScript, [(RedisKey)RedisIdempotencyStore.LockKeyOf(envelopeId)], [(RedisValue)token]);
        }
        catch (Exception exception)
        {
            // The lock expires on its own; failing to release it must not hide the real outcome.
            logger.LogWarning(exception, "Could not release the lock of envelope {EnvelopeId}; it will expire.", envelopeId);
        }
    }
}
