using TradeSimulator.Outbox;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using Npgsql;
using TradeSimulator.EventSourcing;
using NpgsqlTypes;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Orders.Infrastructure.Messaging;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Events;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Infrastructure.Database;

public sealed class PostgresOrderStore(NpgsqlDataSource dataSource, ILogger<PostgresOrderStore> logger)
    : IOrderStore, IOutboxStore<PendingEvent>, IOutboxBacklogReader
{
    private static readonly EventStore Events = OrderEventSerializer.CreateStore();

    public async Task<Result<bool, IOrderError>> InitializeAsync(CancellationToken cancellationToken)
    {
        await using var schemaStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradeSimulator.Orders.Infrastructure.Database.schema.sql");
        if (schemaStream is null)
        {
            logger.LogCritical("Embedded PostgreSQL schema script was not found.");
            return Result<bool, IOrderError>.Error(new OrderPersistenceFailure());
        }

        using var reader = new StreamReader(schemaStream);
        var schema = await reader.ReadToEndAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(schema);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("PostgreSQL order event schema is ready.");
        return true;
    }

    public async Task<Result<Order, IOrderError>> SendAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.UncommittedEvents.Count != 1 || order.UncommittedEvents[0] is not OrderSubmitted sent)
            return new UnsupportedOrderEvent();

        try
        {
            return await InsertAsync(order, cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "order_requests_pkey")
        {
            logger.LogInformation("Duplicate order submission detected; returning the original order.");
            return await GetByRequestHashAsync(sent.RequestHash, cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName is "ux_events_single_submitted" or "ux_events_stream_version")
        {
            return new OrderAlreadyExists();
        }
    }

    public async Task<Result<Order, IOrderError>> AppendAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.UncommittedEvents.Count == 0 || order.UncommittedEvents.Any(e => e is OrderSubmitted))
            return new UnsupportedOrderEvent();

        try
        {
            return await InsertAsync(order, cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "ux_events_stream_version")
        {
            return new ConcurrencyConflict();
        }
    }

    private async Task<Result<Order, IOrderError>> InsertAsync(Order order, CancellationToken cancellationToken)
    {
        const string outboxSql = """
            INSERT INTO event_outbox (position, event_type, payload, traceparent)
            VALUES (@position, @eventType, @envelope, @traceParent);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var firstVersion = order.Version - order.UncommittedEvents.Count + 1;
        if (order.UncommittedEvents.OfType<OrderSubmitted>().SingleOrDefault() is { } submitted)
        {
            await using var request = new NpgsqlCommand(
                "INSERT INTO order_requests (request_hash, stream_id) VALUES (@hash, @stream);", connection, transaction);
            request.Parameters.AddWithValue("hash", NpgsqlDbType.Char, submitted.RequestHash.Value);
            request.Parameters.AddWithValue("stream", submitted.StreamId);
            await request.ExecuteNonQueryAsync(cancellationToken);
        }

        var appended = await Events.AppendAsync(connection, transaction, order.UncommittedEvents, firstVersion, cancellationToken);
        foreach (var entry in appended)
        {
            await using var command = new NpgsqlCommand(outboxSql, connection, transaction);
            command.Parameters.AddWithValue("position", entry.Id);
            command.Parameters.AddWithValue("eventType", entry.Type);
            command.Parameters.Add(new NpgsqlParameter("envelope", NpgsqlDbType.Jsonb)
            {
                Value = OrderEventSerializer.SerializeEnvelope((OrderEvent)entry.Event)
            });
            command.Parameters.AddWithValue("traceParent", (object?)ServiceTelemetry.CurrentTraceParent() ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        order.CommitEvents();
        return order;
    }

    public async Task<IReadOnlyList<StoredOrderEvent>> ReadEventsAsync(OrderId id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT version, event_type, occurred_at, payload::text
            FROM events
            WHERE stream_id = @orderId
            ORDER BY version;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("orderId", id.Guid);
        var events = new List<StoredOrderEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            events.Add(new StoredOrderEvent(reader.GetInt32(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3)));
        return events;
    }

    public async Task<Result<Order, IOrderError>> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        await ReadOrderAsync(id.Guid, cancellationToken);

    public async Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT position, event_type, payload::text, traceparent
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
            pending.Add(new PendingEvent(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return pending;
    }

    public async Task<IReadOnlyList<OutboxBacklog>> ReadOutboxBacklogAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*), COALESCE(EXTRACT(EPOCH FROM now() - MIN(e.occurred_at)), 0)
            FROM event_outbox o JOIN events e ON e.id = o.position
            WHERE o.published_at IS NULL;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return [new OutboxBacklog("commands", reader.GetInt64(0), Convert.ToDouble(reader.GetValue(1)))];
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

    private async Task<Result<Order, IOrderError>> GetByRequestHashAsync(RequestHash hash, CancellationToken cancellationToken)
    {
        const string sql = "SELECT stream_id FROM order_requests WHERE request_hash = @requestHash;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("requestHash", NpgsqlDbType.Char, hash.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid orderId
            ? await ReadOrderAsync(orderId, cancellationToken)
            : new OrderNotFound();
    }

    private async Task<Result<Order, IOrderError>> ReadOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredEvent> stored;
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            stored = await Events.ReadStreamAsync(connection, orderId, cancellationToken);
        }
        catch (CorruptEventStreamException)
        {
            return new CorruptOrderHistory();
        }

        if (stored.Count == 0)
            return new OrderNotFound();
        var legacyOpening = stored.Any(entry => entry.Type == OrderEventSerializer.LegacyOpeningType);
        return Order.Rehydrate(stored.Select(entry => (OrderEvent)entry.Event).ToList(), legacyOpening);
    }
}
