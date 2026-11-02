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
using TradeSimulator.Portfolio.Processors.PortfolioCommands;
using TradeSimulator.Portfolio.Infrastructure.Serialization;

namespace TradeSimulator.Portfolio.Infrastructure.Database;

public sealed class PostgresAssetStore(NpgsqlDataSource dataSource, ILogger<PostgresAssetStore> logger)
    : IAssetStore, IOutbox, IOutboxBacklogReader
{
    public const int SnapshotInterval = 100;
    private static readonly EventStore Events = AssetEventStore.Create();
    private const string EnvelopeConstraint = "ux_event_outbox_envelope";
    private const string ReservationConstraint = "order_reservations_pkey";
    private const string ProcessedConstraint = "processed_order_events_pkey";
    public const string SucceededRoutingKey = "asset.command.succeeded";

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

    public async Task<Result<Asset, IPortfolioError>> FindAsync(AccountId accountId, Ticker ticker, CancellationToken cancellationToken)
    {
        const string findSql = "SELECT asset_id FROM active_assets WHERE account_id = @accountId AND ticker = @ticker;";
        await using var find = dataSource.CreateCommand(findSql);
        find.Parameters.AddWithValue("accountId", accountId.Guid);
        find.Parameters.AddWithValue("ticker", ticker.Value);
        if (await find.ExecuteScalarAsync(cancellationToken) is not Guid assetId)
            return new AssetNotFound();

        return await LoadAsync(assetId, cancellationToken);
    }

    public async Task<Result<Asset, IPortfolioError>> CreateAsync(Asset asset, PortfolioCommandResult result, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertEventsAsync(asset, connection, transaction, cancellationToken);

            const string sql = """
                INSERT INTO active_assets (asset_id, account_id, ticker, version, updated_at)
                VALUES (@assetId, @accountId, @ticker, @version, @now);
                """;
            await using (var command = new NpgsqlCommand(sql, connection, transaction))
            {
                AddAssetParameters(command, asset);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertOutboxAsync(result, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.ConstraintName == EnvelopeConstraint)
        {
            return new DuplicateEnvelope();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new AssetAlreadyExists();
        }

        asset.CommitEvents();
        return asset;
    }

    public async Task<Result<Asset, IPortfolioError>> AppendAsync(Asset asset, PortfolioCommandResult result, CancellationToken cancellationToken)
    {
        if (asset.UncommittedEvents.Count == 0)
            throw new InvalidOperationException("There are no uncommitted events to append.");

        var saved = await PersistAsync(
            asset,
            (connection, transaction, token) => InsertOutboxAsync(result, connection, transaction, token),
            cancellationToken);
        return saved.IsError ? Result<Asset, IPortfolioError>.Error(saved.ErrorValue) : asset;
    }

    public async Task<Result<Asset, IPortfolioError>> ReserveAsync(
        Guid envelopeId, Asset asset, OrderReservation reservation, AssetsBlocked blocked, CancellationToken cancellationToken, bool notify = true)
    {
        if (asset.UncommittedEvents.Count == 0)
            throw new InvalidOperationException("There are no uncommitted events to append.");

        var saved = await PersistAsync(
            asset,
            async (connection, transaction, token) =>
            {
                const string sql = """
                    INSERT INTO order_reservations (order_id, account_id, ticker, blocked, remaining)
                    VALUES (@orderId, @accountId, @ticker, @blocked, @remaining);
                    """;
                await using (var command = new NpgsqlCommand(sql, connection, transaction))
                {
                    command.Parameters.AddWithValue("orderId", reservation.OrderId);
                    command.Parameters.AddWithValue("accountId", reservation.AccountId);
                    command.Parameters.AddWithValue("ticker", reservation.Ticker);
                    command.Parameters.AddWithValue("blocked", reservation.Blocked);
                    command.Parameters.AddWithValue("remaining", reservation.Remaining);
                    await command.ExecuteNonQueryAsync(token);
                }

                if (notify)
                    await InsertReservationOutboxAsync(AssetReservationEvents.BlockedRoutingKey, envelopeId, ReservationResponse(Result<AssetsBlocked, AssetBlockError>.Ok(blocked)), connection, transaction, token);
                return true;
            },
            cancellationToken);
        return saved.IsError ? Result<Asset, IPortfolioError>.Error(saved.ErrorValue) : asset;
    }

    public async Task<bool> RejectReservationAsync(Guid envelopeId, AssetBlockError error, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertReservationOutboxAsync(AssetReservationEvents.FailedRoutingKey, envelopeId, ReservationResponse(Result<AssetsBlocked, AssetBlockError>.Error(error)), connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.ConstraintName == EnvelopeConstraint)
        {
            return false;
        }
    }

    public async Task<OrderReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT account_id, ticker, blocked, remaining, closed_at IS NOT NULL
            FROM order_reservations
            WHERE order_id = @orderId;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("orderId", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new OrderReservation(orderId, reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetBoolean(4))
            : null;
    }

    public async Task<Result<bool, IPortfolioError>> SettleAsync(
        Guid envelopeId, Asset? asset, Guid orderId, long expectedRemaining, long newRemaining, bool close, CancellationToken cancellationToken)
    {
        return await PersistAsync(
            asset,
            async (connection, transaction, token) =>
            {
                await using (var mark = new NpgsqlCommand(
                    "INSERT INTO processed_order_events (envelope_id) VALUES (@id);", connection, transaction))
                {
                    mark.Parameters.AddWithValue("id", envelopeId);
                    await mark.ExecuteNonQueryAsync(token);
                }

                const string sql = """
                    UPDATE order_reservations
                    SET remaining = @newRemaining,
                        closed_at = CASE WHEN @close THEN CURRENT_TIMESTAMP ELSE closed_at END,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE order_id = @orderId AND remaining = @expected AND closed_at IS NULL;
                    """;
                await using var update = new NpgsqlCommand(sql, connection, transaction);
                update.Parameters.AddWithValue("newRemaining", newRemaining);
                update.Parameters.AddWithValue("close", close);
                update.Parameters.AddWithValue("orderId", orderId);
                update.Parameters.AddWithValue("expected", expectedRemaining);
                return await update.ExecuteNonQueryAsync(token) == 1;
            },
            cancellationToken);
    }

    // Appends the asset events (when there are any) and runs the extra writes in one transaction;
    // the extra returns false when its optimistic guard failed.
    private async Task<Result<bool, IPortfolioError>> PersistAsync(
        Asset? asset,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<bool>> extra,
        CancellationToken cancellationToken)
    {
        var hasEvents = asset is { UncommittedEvents.Count: > 0 };
        var previousVersion = hasEvents ? asset!.Version - asset.UncommittedEvents.Count : 0;
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (hasEvents)
            {
                await InsertEventsAsync(asset!, connection, transaction, cancellationToken);

                if (!await UpdateActiveAssetAsync(asset!, previousVersion, connection, transaction, cancellationToken))
                    return new ConcurrencyConflict();

                if (!asset!.IsRemoved && asset.Version / SnapshotInterval > previousVersion / SnapshotInterval)
                    await UpsertSnapshotAsync(asset, connection, transaction, cancellationToken);
            }

            if (!await extra(connection, transaction, cancellationToken))
                return new ConcurrencyConflict();

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.ConstraintName is EnvelopeConstraint or ReservationConstraint or ProcessedConstraint)
        {
            return new DuplicateEnvelope();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new ConcurrencyConflict();
        }

        if (hasEvents)
            asset!.CommitEvents();
        return true;
    }

    public async Task<IReadOnlyList<PendingResponse>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT position, routing_key, payload::text, exchange, traceparent
            FROM event_outbox
            WHERE published_at IS NULL
            ORDER BY position
            LIMIT @limit;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pending = new List<PendingResponse>();
        while (await reader.ReadAsync(cancellationToken))
            pending.Add(new PendingResponse(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
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
        return [new OutboxBacklog("responses", reader.GetInt64(0), Convert.ToDouble(reader.GetValue(1)))];
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

    public async Task<Result<Asset, IPortfolioError>> LoadAsync(Guid assetId, CancellationToken cancellationToken)
    {
        AssetSnapshot? snapshot = null;
        const string snapshotSql = "SELECT payload::text FROM snapshots WHERE stream_id = @assetId;";
        await using (var command = dataSource.CreateCommand(snapshotSql))
        {
            command.Parameters.AddWithValue("assetId", assetId);
            if (await command.ExecuteScalarAsync(cancellationToken) is string payload)
            {
                try
                {
                    snapshot = JsonSerializer.Deserialize<AssetSnapshot>(payload, PortfolioJson.Options);
                }
                catch (JsonException)
                {
                    return new CorruptAssetHistory();
                }
            }
        }

        List<AssetEvent> history;
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var stored = await Events.ReadStreamAsync(connection, assetId, cancellationToken, afterVersion: snapshot?.Version ?? 0);
            history = stored.Select(entry => (AssetEvent)entry.Event).ToList();
        }
        catch (CorruptEventStreamException)
        {
            return new CorruptAssetHistory();
        }

        return snapshot is null ? Asset.Rehydrate(history) : Asset.Restore(snapshot, history);
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

    // The row is the optimistic-concurrency guard (version); an emptied asset is removed.
    private static async Task<bool> UpdateActiveAssetAsync(
        Asset asset, int previousVersion, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var sql = asset.IsRemoved
            ? "DELETE FROM active_assets WHERE asset_id = @assetId AND version = @previousVersion;"
            : """
              UPDATE active_assets
              SET version = @version, updated_at = @now
              WHERE asset_id = @assetId AND version = @previousVersion;
              """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddAssetParameters(command, asset);
        command.Parameters.AddWithValue("previousVersion", previousVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task UpsertSnapshotAsync(
        Asset asset, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO snapshots (stream_id, version, payload)
            VALUES (@assetId, @version, @payload)
            ON CONFLICT (stream_id) DO UPDATE
            SET version = EXCLUDED.version, payload = EXCLUDED.payload, updated_at = CURRENT_TIMESTAMP;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("assetId", asset.Id.Guid);
        command.Parameters.AddWithValue("version", asset.Version);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(asset.ToSnapshot(), PortfolioJson.Options)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AssetReservationResponse ReservationResponse(Result<AssetsBlocked, AssetBlockError> outcome) =>
        new(Guid.CreateVersion7(), AssetReservationEvents.ResultEventType, outcome);

    private static async Task InsertReservationOutboxAsync(
        string routingKey, Guid envelopeId, AssetReservationResponse response,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO event_outbox (routing_key, payload, envelope_id, exchange, traceparent)
            VALUES (@routingKey, @payload, @envelopeId, @exchange, @traceParent);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("routingKey", routingKey);
        command.Parameters.AddWithValue("envelopeId", envelopeId);
        command.Parameters.AddWithValue("traceParent", (object?)ServiceTelemetry.CurrentTraceParent() ?? DBNull.Value);
        command.Parameters.AddWithValue("exchange", AssetReservationEvents.Exchange);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(response, PortfolioJson.Options)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> InsertOutboxAsync(
        PortfolioCommandResult result, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var response = new PortfolioResponse(
            Guid.CreateVersion7(), PortfolioEventTypes.CommandResult,
            Result<PortfolioCommandResult, PortfolioCommandError>.Ok(result));
        const string sql = """
            INSERT INTO event_outbox (routing_key, payload, envelope_id, traceparent)
            VALUES (@routingKey, @payload, @envelopeId, @traceParent);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("routingKey", SucceededRoutingKey);
        command.Parameters.AddWithValue("envelopeId", result.EnvelopeId);
        command.Parameters.AddWithValue("traceParent", (object?)ServiceTelemetry.CurrentTraceParent() ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(response, PortfolioJson.Options)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static void AddAssetParameters(NpgsqlCommand command, Asset asset)
    {
        command.Parameters.AddWithValue("assetId", asset.Id.Guid);
        command.Parameters.AddWithValue("accountId", asset.AccountId.Guid);
        command.Parameters.AddWithValue("ticker", asset.Ticker.Value);
        command.Parameters.AddWithValue("version", asset.Version);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
    }
}
