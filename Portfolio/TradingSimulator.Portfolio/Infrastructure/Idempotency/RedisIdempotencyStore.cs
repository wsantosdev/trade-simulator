using StackExchange.Redis;
using TradingSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradingSimulator.Portfolio.Infrastructure.Idempotency;

// Two keys per envelope:
//  - "{id}"       value 1, TTL 1h: written only after the outcome is durable, so a crash can never leave it orphaned;
//  - "lock:{id}"  owner token, short TTL: in-flight exclusivity that expires by itself if the worker dies.
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis, ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    public static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);

    public async Task<IEnvelopeLease?> TryLockAsync(Guid envelopeId, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var acquired = await database.StringSetAsync(LockKeyOf(envelopeId), token, LockTtl, When.NotExists);
        return acquired ? new RedisEnvelopeLease(database, envelopeId, token, logger) : null;
    }

    public static string KeyOf(Guid envelopeId) => envelopeId.ToString();

    public static string LockKeyOf(Guid envelopeId) => $"lock:{envelopeId}";
}
