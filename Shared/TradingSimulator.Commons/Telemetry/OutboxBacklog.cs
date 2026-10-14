namespace TradingSimulator.Commons.Telemetry;

public sealed record OutboxBacklog(string Outbox, long Pending, double OldestAgeSeconds);

public interface IOutboxBacklogReader
{
    Task<IReadOnlyList<OutboxBacklog>> ReadOutboxBacklogAsync(CancellationToken cancellationToken);
}
