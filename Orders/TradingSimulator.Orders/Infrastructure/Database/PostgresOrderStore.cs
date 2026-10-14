using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using Npgsql;
using NpgsqlTypes;
using TradingSimulator.Commons.Telemetry;
using TradingSimulator.Orders.Infrastructure.Messaging;
using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain;
using TradingSimulator.Orders.Domain.Entities;
using TradingSimulator.Orders.Domain.Events;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Infrastructure.Database;

public sealed class PostgresOrderStore(NpgsqlDataSource dataSource, ILogger<PostgresOrderStore> logger)
    : IOrderStore, IOutbox, IOutboxBacklogReader
{
    public async Task<Result<bool, IOrderError>> InitializeAsync(CancellationToken cancellationToken)
    {
        await using var schemaStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradingSimulator.Orders.Infrastructure.Database.schema.sql");
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
            exception.ConstraintName == "ux_order_events_request_hash")
        {
            logger.LogInformation("Duplicate order submission detected; returning the original order.");
            return await GetByRequestHashAsync(sent.RequestHash, cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName is "ux_order_events_single_submitted" or "ux_order_events_order_sequence")
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
            exception.ConstraintName == "ux_order_events_order_sequence")
        {
            return new ConcurrencyConflict();
        }
    }

    private async Task<Result<Order, IOrderError>> InsertAsync(Order order, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH inserted AS (
                INSERT INTO order_events (order_id, sequence, event_type, payload, request_hash, occurred_at)
                VALUES (@orderId, @sequence, @eventType, @payload, @requestHash, @occurredAt)
                RETURNING position
            )
            INSERT INTO order_outbox (position, event_type, payload, traceparent)
            SELECT position, @eventType, @envelope, @traceParent FROM inserted WHERE @published;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sequence = order.Version - order.UncommittedEvents.Count;
        foreach (var @event in order.UncommittedEvents)
        {
            sequence++;
            var published = OrderEventSerializer.IsPublished(@event);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("orderId", @event.OrderId.Guid);
            command.Parameters.AddWithValue("sequence", sequence);
            command.Parameters.AddWithValue("eventType", @event.GetType().Name);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
            {
                Value = OrderEventSerializer.Serialize(@event)
            });
            command.Parameters.AddWithValue("requestHash", NpgsqlDbType.Char,
                @event is OrderSubmitted sent ? sent.RequestHash.Value : DBNull.Value);
            command.Parameters.AddWithValue("occurredAt", @event.OccurredAt);
            command.Parameters.Add(new NpgsqlParameter("envelope", NpgsqlDbType.Jsonb)
            {
                Value = published ? OrderEventSerializer.SerializeEnvelope(@event) : "null"
            });
            command.Parameters.AddWithValue("published", published);
            command.Parameters.AddWithValue("traceParent", (object?)ServiceTelemetry.CurrentTraceParent() ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertReadModelAsync(order, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        order.MarkCommitted();
        return order;
    }

    private static async Task UpsertReadModelAsync(
        Order order, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO order_read_model (
                order_id, account_id, ticker, side, type, quantity, price, state, executed_quantity,
                remaining_quantity, average_price, last_price, last_trade_at, sent_at, last_action_at, version)
            VALUES (
                @orderId, @accountId, @ticker, @side, @type, @quantity, @price, @state, @executedQuantity,
                @remainingQuantity, @averagePrice, @lastPrice, @lastTradeAt, @sentAt, @lastActionAt, @version)
            ON CONFLICT (order_id) DO UPDATE SET
                price = EXCLUDED.price,
                state = EXCLUDED.state,
                executed_quantity = EXCLUDED.executed_quantity,
                remaining_quantity = EXCLUDED.remaining_quantity,
                average_price = EXCLUDED.average_price,
                last_price = EXCLUDED.last_price,
                last_trade_at = EXCLUDED.last_trade_at,
                last_action_at = EXCLUDED.last_action_at,
                version = EXCLUDED.version;
            """;
        var view = OrderView.From(order);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("orderId", view.Id);
        command.Parameters.AddWithValue("accountId", view.AccountId);
        command.Parameters.AddWithValue("ticker", view.Ticker);
        command.Parameters.AddWithValue("side", view.Side.ToString());
        command.Parameters.AddWithValue("type", view.Type.ToString());
        command.Parameters.AddWithValue("quantity", view.Quantity);
        command.Parameters.AddWithValue("price", NpgsqlDbType.Numeric, (object?)view.Price ?? DBNull.Value);
        command.Parameters.AddWithValue("state", view.State.ToString());
        command.Parameters.AddWithValue("executedQuantity", view.ExecutedQuantity);
        command.Parameters.AddWithValue("remainingQuantity", view.RemainingQuantity);
        command.Parameters.AddWithValue("averagePrice", NpgsqlDbType.Numeric, (object?)view.AveragePrice ?? DBNull.Value);
        command.Parameters.AddWithValue("lastPrice", NpgsqlDbType.Numeric, (object?)view.LastPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("lastTradeAt", NpgsqlDbType.TimestampTz, (object?)view.LastTradeAt ?? DBNull.Value);
        command.Parameters.AddWithValue("sentAt", view.SentAt);
        command.Parameters.AddWithValue("lastActionAt", view.LastActionAt);
        command.Parameters.AddWithValue("version", order.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Result<Order, IOrderError>> GetAsync(OrderId id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_type, payload::text
            FROM order_events
            WHERE order_id = @orderId
            ORDER BY sequence;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("orderId", id.Guid);
        return await ReadOrderAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT position, event_type, payload::text, traceparent
            FROM order_outbox
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
            FROM order_outbox o JOIN order_events e ON e.position = o.position
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
            UPDATE order_outbox
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
        const string sql = """
            SELECT event_type, payload::text
            FROM order_events
            WHERE order_id = (SELECT order_id FROM order_events WHERE request_hash = @requestHash)
            ORDER BY sequence;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("requestHash", NpgsqlDbType.Char, hash.Value);
        return await ReadOrderAsync(command, cancellationToken);
    }

    private static async Task<Result<Order, IOrderError>> ReadOrderAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var history = new List<OrderEvent>();
        var legacyOpening = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var eventType = reader.GetString(0);
                var payload = reader.GetString(1);
                legacyOpening |= eventType == OrderEventSerializer.LegacyOpeningType;
                var @event = OrderEventSerializer.Deserialize(eventType, payload);
                if (@event is null)
                    return new CorruptOrderHistory();
                history.Add(@event);
            }
        }

        return history.Count == 0 ? new OrderNotFound() : Order.Rehydrate(history, legacyOpening);
    }
}
