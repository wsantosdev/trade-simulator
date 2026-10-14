using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using TradingSimulator.Exchange.Features.ImportInstruments;

namespace TradingSimulator.Exchange.Infrastructure.Database;

public sealed class PostgresInstrumentStore(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider,
    ILogger<PostgresInstrumentStore> logger) : IInstrumentStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var schemaStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradingSimulator.Exchange.Infrastructure.Database.schema.sql");
        if (schemaStream is null)
            throw new InvalidOperationException("Embedded PostgreSQL schema script was not found.");

        using var reader = new StreamReader(schemaStream);
        var schema = await reader.ReadToEndAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(schema);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("PostgreSQL exchange schema is ready.");
    }

    public async Task<UpsertOutcome> UpsertAsync(
        IReadOnlyCollection<InstrumentRecord> instruments, CancellationToken cancellationToken)
    {
        // xmax = 0 identifies freshly inserted rows; unchanged names are not rewritten.
        const string sql = """
            INSERT INTO instruments (instrument_id, ticker, name, updated_at)
            SELECT * FROM unnest(@ids, @tickers, @names, @updatedAt)
            ON CONFLICT (ticker) DO UPDATE
                SET name = EXCLUDED.name, updated_at = EXCLUDED.updated_at
                WHERE instruments.name IS DISTINCT FROM EXCLUDED.name
            RETURNING (xmax = 0);
            """;

        var now = timeProvider.GetUtcNow();
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            { Value = instruments.Select(i => i.Id.Guid).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("tickers", NpgsqlDbType.Array | NpgsqlDbType.Text)
            { Value = instruments.Select(i => i.Ticker.Value).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("names", NpgsqlDbType.Array | NpgsqlDbType.Text)
            { Value = instruments.Select(i => i.Name).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("updatedAt", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
            { Value = instruments.Select(_ => now).ToArray() });

        int inserted = 0, updated = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetBoolean(0))
                inserted++;
            else
                updated++;
        }

        return new UpsertOutcome(inserted, updated);
    }
}
