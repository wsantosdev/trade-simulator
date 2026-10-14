namespace TradingSimulator.Account.Infrastructure.Messaging;

internal static class OutboxPublishing
{
    // Upper bound to publish and mark a single outbox item, also applied while stopping.
    public static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(10);
}
