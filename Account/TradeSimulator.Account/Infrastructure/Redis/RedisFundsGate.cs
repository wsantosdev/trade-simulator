using StackExchange.Redis;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Processors.OrderFunds;

namespace TradeSimulator.Account.Infrastructure.Redis;

// Redis executes each Lua script atomically on a single thread, so the check-and-decrement cannot race.
public sealed class RedisFundsGate(IConnectionMultiplexer redis) : IFundsGate
{
    private static readonly TimeSpan LedgerTtl = TimeSpan.FromDays(1);

    // KEYS: balance, reservation. ARGV: cents, ttl seconds. Returns 1 reserved, 0 insufficient, -1 balance not cached.
    private const string ReserveScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then return 1 end
        local available = redis.call('GET', KEYS[1])
        if not available then return -1 end
        if tonumber(available) < tonumber(ARGV[1]) then return 0 end
        redis.call('DECRBY', KEYS[1], ARGV[1])
        redis.call('SET', KEYS[2], ARGV[1], 'EX', ARGV[2])
        return 1
        """;

    // Gives back what the reservation took and drops the cached balance so the next order reloads it.
    private const string CompensateScript = """
        local taken = redis.call('GET', KEYS[2])
        if taken then
            redis.call('DEL', KEYS[2])
        end
        redis.call('DEL', KEYS[1])
        return 1
        """;

    private IDatabase Db => redis.GetDatabase();

    public async Task<FundsGateResult> TryReserveAsync(AccountId accountId, OrderId orderId, long cents, CancellationToken cancellationToken)
    {
        var result = (long)await Db.ScriptEvaluateAsync(
            ReserveScript,
            [BalanceKey(accountId), ReservationKey(orderId)],
            [cents, (long)LedgerTtl.TotalSeconds]);
        return result switch
        {
            1 => FundsGateResult.Reserved,
            0 => FundsGateResult.Insufficient,
            _ => FundsGateResult.Miss
        };
    }

    public Task HydrateAsync(AccountId accountId, long availableCents, CancellationToken cancellationToken) =>
        Db.StringSetAsync(BalanceKey(accountId), availableCents, when: When.NotExists);

    public async Task CompensateAsync(AccountId accountId, OrderId orderId, CancellationToken cancellationToken) =>
        await Db.ScriptEvaluateAsync(CompensateScript, [BalanceKey(accountId), ReservationKey(orderId)]);

    public Task InvalidateAsync(AccountId accountId, CancellationToken cancellationToken) =>
        Db.KeyDeleteAsync(BalanceKey(accountId));

    private static RedisKey BalanceKey(AccountId accountId) => $"account:funds:{accountId.Guid:N}";
    private static RedisKey ReservationKey(OrderId orderId) => $"account:reservation:{orderId.Guid:N}";
}
