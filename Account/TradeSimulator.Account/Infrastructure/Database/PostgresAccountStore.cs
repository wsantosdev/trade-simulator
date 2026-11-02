using TradeSimulator.Account.Infrastructure.Telemetry;
using TradeSimulator.Commons.Telemetry;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using Npgsql;
using TradeSimulator.EventSourcing;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Infrastructure.Messaging;
using TradeSimulator.Account.Processors.OrderFunds;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.Entities;
using TradeSimulator.Account.Domain.Events;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Infrastructure.Serialization;

namespace TradeSimulator.Account.Infrastructure.Database;

public sealed partial class PostgresAccountStore(
    string connectionString,
    ILogger<PostgresAccountStore> logger,
    IFundsGate? fundsGate = null) : IAccountStore, IOutbox, IOrderFundsStore, IResponseOutbox, IOutboxBacklogReader
{
    private const int SnapshotInterval = 10;
    private static readonly EventStore Events = CreateEventStore();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<bool, IAccountError>> InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var schemaResource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradeSimulator.Account.Infrastructure.Database.schema.sql");
        if (schemaResource is null)
        {
            logger.LogCritical("Embedded PostgreSQL schema script was not found.");
            return Result<bool, IAccountError>.Error(new AccountPersistenceFailure());
        }
        await using var schemaStream = schemaResource;
        using var reader = new StreamReader(schemaStream);
        var schema = await reader.ReadToEndAsync(cancellationToken);

        await using var command = new NpgsqlCommand(schema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("PostgreSQL account event schema is ready.");
        return true;
    }

    public async Task<Result<Domain.Entities.Account, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken)
    {
        var opened = Domain.Entities.Account.Open(id);
        if (opened.IsError)
            return opened;

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var account = opened.ResultValue;
            var persisted = await PersistOpeningEventAsync(connection, account, cancellationToken);
            if (persisted.IsError)
                return Result<Domain.Entities.Account, IAccountError>.Error(persisted.ErrorValue);
            account.CommitEvents();
            return account;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName is "ux_events_stream_version")
        {
            logger.LogInformation(exception, "Account {AccountId} is already open.", id.Guid);
            return Result<Domain.Entities.Account, IAccountError>.Error(new AccountAlreadyExists());
        }
    }

    public async Task<Result<Domain.Entities.Account, IAccountError>> ExecuteAsync(
        AccountId id,
        Func<Domain.Entities.Account, Result<Domain.Entities.Account, IAccountError>> operation,
        CancellationToken cancellationToken)
    {
        var waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        await _gate.WaitAsync(cancellationToken);
        AccountTelemetry.GateWait(System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted));
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var loadedResult = await LoadAccountAsync(connection, id, cancellationToken);
            if (loadedResult.IsError)
                return Result<Domain.Entities.Account, IAccountError>.Error(loadedResult.ErrorValue);

            var loaded = loadedResult.ResultValue;
            var result = operation(loaded.Account);
            if (result.IsError)
                return result;

            var persisted = await AppendAsync(connection, loaded, cancellationToken);
            if (persisted.IsError)
                return Result<Domain.Entities.Account, IAccountError>.Error(persisted.ErrorValue);
            loaded.Account.CommitEvents();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Domain.Entities.Account, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT version, available_balance, blocked_balance
            FROM account_balances
            WHERE account_id = @id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id.Guid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Result<Domain.Entities.Account, IAccountError>.Error(new AccountNotFound());

        var state = new AccountSnapshot(id, reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
        return Domain.Entities.Account.Rehydrate(id, state, []);
    }

    public async Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT position, event_type, payload::text, traceparent
            FROM event_outbox
            WHERE published_at IS NULL
            ORDER BY position
            LIMIT @limit;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pending = new List<PendingEvent>();
        while (await reader.ReadAsync(cancellationToken))
            pending.Add(new PendingEvent(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return pending;
    }

    public async Task MarkPublishedAsync(long position, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE event_outbox
            SET published_at = @publishedAt
            WHERE position = @position AND published_at IS NULL;
            """;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("publishedAt", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("position", position);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static EventStore CreateEventStore()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new AccountIdJsonConverter());
        json.Converters.Add(new OrderIdJsonConverter());
        return new EventStore(new EventStoreOptions(
            json, [typeof(AccountEvent).Assembly]));
    }

    private static async Task InsertOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AppendedEvent entry,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO event_outbox(position, event_type, payload, traceparent)
            VALUES(@position, @eventType, @payload::jsonb, @traceParent);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("position", entry.Id);
        command.Parameters.AddWithValue("eventType", entry.Type);
        command.Parameters.AddWithValue("payload", entry.Payload);
        command.Parameters.AddWithValue("traceParent", (object?)ServiceTelemetry.CurrentTraceParent() ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Result<LoadedAccount, IAccountError>> LoadAccountAsync(
        NpgsqlConnection connection,
        AccountId id,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadSnapshotAsync(connection, id, cancellationToken);
        var eventsResult = await LoadEventsAsync(connection, id, snapshot?.State.Version ?? 0, cancellationToken);
        if (eventsResult.IsError)
            return Result<LoadedAccount, IAccountError>.Error(eventsResult.ErrorValue);
        var events = eventsResult.ResultValue;
        if (snapshot is null && events.Count == 0)
            return Result<LoadedAccount, IAccountError>.Error(new AccountNotFound());

        var accountResult = snapshot is null
            ? Domain.Entities.Account.Rehydrate(id, events)
            : Domain.Entities.Account.Rehydrate(id, snapshot.State, events);
        if (accountResult.IsError)
            return Result<LoadedAccount, IAccountError>.Error(accountResult.ErrorValue);
        return new LoadedAccount(accountResult.ResultValue, events.Count);
    }

    private static async Task<StoredSnapshot?> LoadSnapshotAsync(
        NpgsqlConnection connection,
        AccountId id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT version, available_balance, provisioned_balance
            FROM snapshots
            WHERE stream_id = @id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id.Guid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StoredSnapshot(new AccountSnapshot(id, reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2)));
    }

    private static async Task<Result<List<AccountEvent>, IAccountError>> LoadEventsAsync(
        NpgsqlConnection connection,
        AccountId id,
        int afterVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var stored = await Events.ReadStreamAsync(connection, id.Guid, cancellationToken, afterVersion);
            return stored.Select(entry => (AccountEvent)entry.Event).ToList();
        }
        catch (CorruptEventStreamException)
        {
            return Result<List<AccountEvent>, IAccountError>.Error(new CorruptAccountHistory());
        }
    }

    private static async Task<Result<bool, IAccountError>> AppendAsync(
        NpgsqlConnection connection,
        LoadedAccount loaded,
        CancellationToken cancellationToken)
    {
        if (loaded.Account.UncommittedEvents.Count == 0)
            return true;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var appended = await AppendEventsAsync(connection, transaction, loaded, cancellationToken);
        if (appended.IsError)
            return appended;

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<Result<bool, IAccountError>> AppendEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LoadedAccount loaded,
        CancellationToken cancellationToken)
    {
        var account = loaded.Account;
        var appended = await Events.AppendAsync(connection, transaction, account.UncommittedEvents, account.Version - account.UncommittedEvents.Count + 1, cancellationToken);
        foreach (var entry in appended)
            await InsertOutboxAsync(connection, transaction, entry, cancellationToken);

        var balance = await UpsertBalanceAsync(connection, transaction, account, cancellationToken);
        if (balance.IsError)
            return balance;

        if (loaded.EventsSinceSnapshot + account.UncommittedEvents.Count >= SnapshotInterval)
        {
            var snapshot = await UpsertSnapshotAsync(connection, transaction, account, cancellationToken);
            if (snapshot.IsError)
                return Result<bool, IAccountError>.Error(snapshot.ErrorValue);
        }

        return true;
    }

    // The read model changes in the same transaction as the events: balances are never stale.
    private static async Task<Result<bool, IAccountError>> UpsertBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Domain.Entities.Account account,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO account_balances(account_id, version, available_balance, blocked_balance)
            VALUES(@accountId, @version, @availableBalance, @blockedBalance)
            ON CONFLICT (account_id) DO UPDATE
            SET version = EXCLUDED.version,
                available_balance = EXCLUDED.available_balance,
                blocked_balance = EXCLUDED.blocked_balance,
                updated_at = CURRENT_TIMESTAMP;
            """;
        var snapshotResult = account.CreateSnapshot();
        if (snapshotResult.IsError)
            return Result<bool, IAccountError>.Error(snapshotResult.ErrorValue);
        var state = snapshotResult.ResultValue;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("accountId", state.AccountId.Guid);
        command.Parameters.AddWithValue("version", state.Version);
        command.Parameters.AddWithValue("availableBalance", state.AvailableBalance);
        command.Parameters.AddWithValue("blockedBalance", state.ProvisionedBalance);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task<Result<bool, IAccountError>> UpsertSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Domain.Entities.Account account,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO snapshots(stream_id, version, available_balance, provisioned_balance)
            VALUES(@accountId, @version, @availableBalance, @provisionedBalance)
            ON CONFLICT (stream_id) DO UPDATE
            SET version = EXCLUDED.version,
                available_balance = EXCLUDED.available_balance,
                provisioned_balance = EXCLUDED.provisioned_balance,
                updated_at = CURRENT_TIMESTAMP;
            """;
        var snapshotResult = account.CreateSnapshot();
        if (snapshotResult.IsError)
            return Result<bool, IAccountError>.Error(snapshotResult.ErrorValue);
        var snapshot = snapshotResult.ResultValue;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("accountId", snapshot.AccountId.Guid);
        command.Parameters.AddWithValue("version", snapshot.Version);
        command.Parameters.AddWithValue("availableBalance", snapshot.AvailableBalance);
        command.Parameters.AddWithValue("provisionedBalance", snapshot.ProvisionedBalance);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task<Result<bool, IAccountError>> PersistOpeningEventAsync(
        NpgsqlConnection connection,
        Domain.Entities.Account account,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var appended = await Events.AppendAsync(connection, transaction, [account.UncommittedEvents.Single()], 1, cancellationToken);
        await InsertOutboxAsync(connection, transaction, appended[0], cancellationToken);
        var balance = await UpsertBalanceAsync(connection, transaction, account, cancellationToken);
        if (balance.IsError)
            return balance;
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private sealed record LoadedAccount(Domain.Entities.Account Account, int EventsSinceSnapshot);

    private sealed record StoredSnapshot(AccountSnapshot State);
}
