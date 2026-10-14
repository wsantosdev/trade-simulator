using Npgsql;
using TradingSimulator.Commons.Telemetry;

namespace TradingSimulator.Account.Infrastructure.Database;

public sealed partial class PostgresAccountStore
{
    private const string BacklogSql = """
        SELECT 'events', COUNT(*), COALESCE(EXTRACT(EPOCH FROM now() - MIN(e.occurred_at)), 0)
        FROM event_outbox o JOIN account_events e ON e.position = o.position
        WHERE o.published_at IS NULL
        UNION ALL
        SELECT 'responses', COUNT(*), COALESCE(EXTRACT(EPOCH FROM now() - MIN(created_at)), 0)
        FROM response_outbox WHERE published_at IS NULL
        """;

    public async Task<IReadOnlyList<OutboxBacklog>> ReadOutboxBacklogAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(BacklogSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<OutboxBacklog>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new OutboxBacklog(reader.GetString(0), reader.GetInt64(1), Convert.ToDouble(reader.GetValue(2))));
        return result;
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
