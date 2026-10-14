using System.Text.Json;
using Moonad;
using Npgsql;
using TradingSimulator.Account.Common;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.ValueObjects;
using TradingSimulator.Account.Infrastructure.Messaging;
using TradingSimulator.Account.Infrastructure.Serialization;

namespace TradingSimulator.Account.Infrastructure.Database;

public sealed partial class PostgresAccountStore
{
    public async Task<Result<OrderFundsOutcome, IAccountError>> ReserveAsync(
        Guid envelopeId,
        OrderId orderId,
        AccountId accountId,
        decimal amount,
        FundsReservationResponse success,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            if (await IsProcessedAsync(connection, null, envelopeId, cancellationToken))
                return OrderFundsOutcome.Duplicate;
            if (await OrderKnownAsync(connection, orderId, cancellationToken))
                return await MarkOnlyAsync(connection, envelopeId, OrderFundsOutcome.Duplicate, cancellationToken);

            var loadedResult = await LoadAccountAsync(connection, accountId, cancellationToken);
            if (loadedResult.IsError)
                return Result<OrderFundsOutcome, IAccountError>.Error(loadedResult.ErrorValue);
            var loaded = loadedResult.ResultValue;
            var reserved = loaded.Account.Reserve(amount, orderId);
            if (reserved.IsError)
                return Result<OrderFundsOutcome, IAccountError>.Error(reserved.ErrorValue);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (!await TryMarkProcessedAsync(connection, transaction, envelopeId, cancellationToken))
                return OrderFundsOutcome.Duplicate;

            var appended = await AppendEventsAsync(connection, transaction, loaded, cancellationToken);
            if (appended.IsError)
                return Result<OrderFundsOutcome, IAccountError>.Error(appended.ErrorValue);

            const string insertSql = """
                INSERT INTO order_reservations(order_id, account_id, reserved_amount, remaining_amount)
                VALUES(@orderId, @accountId, @amount, @amount);
                """;
            await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
            {
                insert.Parameters.AddWithValue("orderId", orderId.Guid);
                insert.Parameters.AddWithValue("accountId", accountId.Guid);
                insert.Parameters.AddWithValue("amount", amount);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await EnqueueResponseAsync(connection, transaction, FundsEventTypes.ReservedRoutingKey, success, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OrderFundsOutcome.Applied;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OrderFundsOutcome> RejectAsync(
        Guid envelopeId,
        OrderId orderId,
        string reason,
        FundsReservationResponse? failure,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            if (await IsProcessedAsync(connection, null, envelopeId, cancellationToken))
                return OrderFundsOutcome.Duplicate;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (!await TryMarkProcessedAsync(connection, transaction, envelopeId, cancellationToken))
                return OrderFundsOutcome.Duplicate;

            const string sql = """
                INSERT INTO ignored_orders(order_id, reason)
                SELECT @orderId, @reason
                WHERE NOT EXISTS (SELECT 1 FROM order_reservations WHERE order_id = @orderId)
                ON CONFLICT (order_id) DO NOTHING;
                """;
            await using (var command = new NpgsqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("orderId", orderId.Guid);
                command.Parameters.AddWithValue("reason", reason);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (failure is not null)
                await EnqueueResponseAsync(connection, transaction, FundsEventTypes.FailedRoutingKey, failure, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return OrderFundsOutcome.Applied;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Result<OrderFundsOutcome, IAccountError>> DebitAsync(
        Guid envelopeId,
        OrderId orderId,
        decimal amount,
        bool final,
        CancellationToken cancellationToken) =>
        SettleAsync(envelopeId, orderId, amount, final, release: false, cancellationToken);

    public Task<Result<OrderFundsOutcome, IAccountError>> ReleaseAsync(
        Guid envelopeId,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        SettleAsync(envelopeId, orderId, 0m, final: true, release: true, cancellationToken);

    private async Task<Result<OrderFundsOutcome, IAccountError>> SettleAsync(
        Guid envelopeId,
        OrderId orderId,
        decimal amount,
        bool final,
        bool release,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            if (await IsProcessedAsync(connection, null, envelopeId, cancellationToken))
                return OrderFundsOutcome.Duplicate;

            var reservation = await ReadReservationAsync(connection, null, orderId, forUpdate: false, cancellationToken);
            if (reservation is null)
            {
                return await IsIgnoredAsync(connection, orderId, cancellationToken)
                    ? await MarkOnlyAsync(connection, envelopeId, OrderFundsOutcome.Ignored, cancellationToken)
                    : OrderFundsOutcome.UnknownOrder;
            }

            if (reservation.ClosedAt is not null)
                return await MarkOnlyAsync(connection, envelopeId, OrderFundsOutcome.Ignored, cancellationToken);

            var accountId = AccountId.TryCreate(reservation.AccountId);
            if (accountId.IsError)
                return Result<OrderFundsOutcome, IAccountError>.Error(accountId.ErrorValue);
            var loadedResult = await LoadAccountAsync(connection, accountId.ResultValue, cancellationToken);
            if (loadedResult.IsError)
                return Result<OrderFundsOutcome, IAccountError>.Error(loadedResult.ErrorValue);
            var loaded = loadedResult.ResultValue;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (!await TryMarkProcessedAsync(connection, transaction, envelopeId, cancellationToken))
                return OrderFundsOutcome.Duplicate;

            var locked = await ReadReservationAsync(connection, transaction, orderId, forUpdate: true, cancellationToken);
            if (locked is null || locked.ClosedAt is not null)
                return OrderFundsOutcome.Ignored;

            var remaining = locked.Remaining;
            var debit = release ? 0m : Math.Min(amount, remaining);
            if (debit > 0)
            {
                var debited = loaded.Account.Debit(debit, orderId);
                if (debited.IsError)
                    return Result<OrderFundsOutcome, IAccountError>.Error(debited.ErrorValue);
                remaining -= debit;
            }

            var close = final || remaining == 0;
            if (close && remaining > 0)
            {
                var released = loaded.Account.Release(remaining, orderId);
                if (released.IsError)
                    return Result<OrderFundsOutcome, IAccountError>.Error(released.ErrorValue);
                remaining = 0;
            }

            if (loaded.Account.UncommittedEvents.Count > 0)
            {
                var appended = await AppendEventsAsync(connection, transaction, loaded, cancellationToken);
                if (appended.IsError)
                    return Result<OrderFundsOutcome, IAccountError>.Error(appended.ErrorValue);
            }

            const string updateSql = """
                UPDATE order_reservations
                SET remaining_amount = @remaining,
                    closed_at = CASE WHEN @close THEN CURRENT_TIMESTAMP ELSE closed_at END,
                    updated_at = CURRENT_TIMESTAMP
                WHERE order_id = @orderId;
                """;
            await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("remaining", remaining);
                update.Parameters.AddWithValue("close", close);
                update.Parameters.AddWithValue("orderId", orderId.Guid);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return OrderFundsOutcome.Applied;
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<IReadOnlyList<PendingResponse>> IResponseOutbox.ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT position, routing_key, payload::text
            FROM response_outbox
            WHERE published_at IS NULL
            ORDER BY position
            LIMIT @limit;
            """;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pending = new List<PendingResponse>();
        while (await reader.ReadAsync(cancellationToken))
            pending.Add(new PendingResponse(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return pending;
    }

    async Task IResponseOutbox.MarkPublishedAsync(long position, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE response_outbox
            SET published_at = @publishedAt
            WHERE position = @position AND published_at IS NULL;
            """;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("publishedAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("position", position);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnqueueResponseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string routingKey,
        FundsReservationResponse response,
        CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO response_outbox(routing_key, payload) VALUES(@routingKey, @payload::jsonb);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("routingKey", routingKey);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(response, AccountJson.Options));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsProcessedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid envelopeId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM processed_messages WHERE envelope_id = @id);", connection, transaction);
        command.Parameters.AddWithValue("id", envelopeId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> TryMarkProcessedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid envelopeId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO processed_messages(envelope_id) VALUES(@id) ON CONFLICT DO NOTHING;", connection, transaction);
        command.Parameters.AddWithValue("id", envelopeId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<OrderFundsOutcome> MarkOnlyAsync(
        NpgsqlConnection connection,
        Guid envelopeId,
        OrderFundsOutcome outcome,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO processed_messages(envelope_id) VALUES(@id) ON CONFLICT DO NOTHING;", connection);
        command.Parameters.AddWithValue("id", envelopeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return outcome;
    }

    private static async Task<bool> OrderKnownAsync(
        NpgsqlConnection connection,
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM order_reservations WHERE order_id = @id)
                OR EXISTS (SELECT 1 FROM ignored_orders WHERE order_id = @id);
            """, connection);
        command.Parameters.AddWithValue("id", orderId.Guid);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> IsIgnoredAsync(
        NpgsqlConnection connection,
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM ignored_orders WHERE order_id = @id);", connection);
        command.Parameters.AddWithValue("id", orderId.Guid);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<StoredReservation?> ReadReservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        OrderId orderId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = "SELECT account_id, remaining_amount, closed_at FROM order_reservations WHERE order_id = @id"
            + (forUpdate ? " FOR UPDATE;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", orderId.Guid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new StoredReservation(
            reader.GetGuid(0),
            reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private sealed record StoredReservation(Guid AccountId, decimal Remaining, DateTimeOffset? ClosedAt);
}
