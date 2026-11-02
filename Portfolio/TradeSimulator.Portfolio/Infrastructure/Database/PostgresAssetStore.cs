using TradeSimulator.Outbox;
using TradeSimulator.Commons.Telemetry;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using Npgsql;
using TradeSimulator.EventSourcing;
using NpgsqlTypes;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.Events;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Infrastructure.Messaging;
using TradeSimulator.Portfolio.Infrastructure.Serialization;

namespace TradeSimulator.Portfolio.Infrastructure.Database;

public sealed class PostgresAssetStore(NpgsqlDataSource dataSource, ILogger<PostgresAssetStore> logger)
    : IAssetStore, IOutboxStore<PendingEvent>, IOutboxBacklogReader
{
    private static readonly EventStore Events = AssetEventStore.Create();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var schemaStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradeSimulator.Portfolio.Infrastructure.Database.schema.sql");
        if (schemaStream is null)
            throw new InvalidOperationException("Embedded PostgreSQL schema script was not found.");

        using var reader = new StreamReader(schemaStream);
        var schema = await reader.ReadToEndAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(schema);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("PostgreSQL portfolio schema is ready.");
    }

    // The operation receives the asset (null when its stream does not exist yet) and decides what happens.
    public async Task<Result<Asset, IPortfolioError>> ExecuteAsync(
        AssetId id, Func<Asset?, Result<Asset, IPortfolioError>> operation, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id, 0));", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", id.Guid.ToString());
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var result = operation(await LoadAsync(connection, id, cancellationToken));
        if (result.IsError)
            return result;

        var asset = result.ResultValue;
        if (asset.UncommittedEvents.Count > 0)
            await InsertEventsAsync(asset, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        asset.CommitEvents();
        return asset;
    }

    public async Task<Result<Asset, IPortfolioError>> GetAsync(AssetId id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var asset = await LoadAsync(connection, id, cancellationToken);
        return asset is null ? new AssetNotFound() : asset;
    }

    public async Task<Result<AssetId, IPortfolioError>> FindByReservationAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT stream_id FROM events WHERE event_type = 'AssetBlocked' AND payload->>'reservationId' = @id;");
        command.Parameters.AddWithValue("id", reservationId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is Guid stream
            ? AssetId.TryCreate(stream)
            : new ReservationNotFound();
    }

    private static async Task<Asset?> LoadAsync(NpgsqlConnection connection, AssetId id, CancellationToken cancellationToken)
    {
        var stored = (await Events.ReadStreamAsync(connection, id.Guid, cancellationToken)).ToList();
        return stored.Count == 0 ? null : Asset.Rehydrate(stored.Select(entry => (AssetEvent)entry.Event).ToList()).ResultValue;
    }

    public async Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT position, routing_key, payload::text, traceparent
            FROM event_outbox
            WHERE published_at IS NULL
            ORDER BY position
            LIMIT @limit;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pending = new List<PendingEvent>();
        while (await reader.ReadAsync(cancellationToken))
            pending.Add(new PendingEvent(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return pending;
    }

    public async Task<IReadOnlyList<OutboxBacklog>> ReadOutboxBacklogAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*), COALESCE(EXTRACT(EPOCH FROM now() - MIN(created_at)), 0)
            FROM event_outbox WHERE published_at IS NULL;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return [new OutboxBacklog("events", reader.GetInt64(0), Convert.ToDouble(reader.GetValue(1)))];
    }

    public async Task MarkPublishedAsync(long position, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE event_outbox
            SET published_at = @publishedAt
            WHERE position = @position AND published_at IS NULL;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("publishedAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("position", position);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Every appended event is also queued in the outbox (same transaction) so the read model can react to it.
    private static async Task InsertEventsAsync(
        Asset asset, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var firstVersion = asset.Version - asset.UncommittedEvents.Count + 1;
        var appended = await Events.AppendAsync(connection, transaction, asset.UncommittedEvents, firstVersion, cancellationToken);
        const string sql = """
            INSERT INTO event_outbox (routing_key, payload, traceparent)
            VALUES (@routingKey, @payload, @traceParent);
            """;
        for (var i = 0; i < appended.Count; i++)
        {
            var changed = new AssetChanged(asset.Id.Guid, firstVersion + i, appended[i].Type);
            var envelope = new EventEnvelope<AssetChanged>(Guid.CreateVersion7(), AssetChanged.EventType, changed);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("routingKey", AssetChanged.RoutingKey);
            command.Parameters.AddWithValue("traceParent", (object?)ServiceTelemetry.CurrentTraceParent() ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(envelope, PortfolioJson.Options)
            });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
