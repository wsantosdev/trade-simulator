using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moonad;
using Npgsql;
using TradingSimulator.Account.Application;
using TradingSimulator.Account.Domain;
using TradingSimulator.Account.Domain.Features.AccountCreation;
using TradingSimulator.Account.Domain.Features.Debits;
using TradingSimulator.Account.Domain.Features.Deposits;
using TradingSimulator.Account.Domain.Features.Releases;
using TradingSimulator.Account.Domain.Features.Reservations;
using TradingSimulator.Account.Domain.Features.Withdrawals;

namespace TradingSimulator.Account.Infrastructure;

public sealed partial class PostgresAccountStore(
    string connectionString,
    ILogger<PostgresAccountStore> logger) : IAccountStore, IOutbox, IOrderFundsStore, IResponseOutbox
{
    private const int SnapshotInterval = 1_000;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<bool, IAccountError>> InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var schemaResource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradingSimulator.Account.Infrastructure.Database.schema.sql");
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

    public async Task<Result<Domain.Account, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken)
    {
        var opened = Domain.Account.Open(id);
        if (opened.IsError)
            return opened;

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var account = opened.ResultValue;
            var persisted = await PersistOpeningEventAsync(connection, account, cancellationToken);
            if (persisted.IsError)
                return Result<Domain.Account, IAccountError>.Error(persisted.ErrorValue);
            account.MarkCommitted();
            return account;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "ux_account_events_single_open")
        {
            logger.LogInformation(exception, "Account {AccountId} is already open.", id.Guid);
            return Result<Domain.Account, IAccountError>.Error(new AccountAlreadyExists());
        }
    }

    public async Task<Result<Domain.Account, IAccountError>> ExecuteAsync(
        AccountId id,
        Func<Domain.Account, Result<Domain.Account, IAccountError>> operation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var loadedResult = await LoadAccountAsync(connection, id, cancellationToken);
            if (loadedResult.IsError)
                return Result<Domain.Account, IAccountError>.Error(loadedResult.ErrorValue);

            var loaded = loadedResult.ResultValue;
            var result = operation(loaded.Account);
            if (result.IsError)
                return result;

            var persisted = await AppendAsync(connection, loaded, cancellationToken);
            if (persisted.IsError)
                return Result<Domain.Account, IAccountError>.Error(persisted.ErrorValue);
            loaded.Account.MarkCommitted();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Domain.Account, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var loaded = await LoadAccountAsync(connection, id, cancellationToken);
        return loaded.IsError
            ? Result<Domain.Account, IAccountError>.Error(loaded.ErrorValue)
            : loaded.ResultValue.Account;
    }

    public async Task<IReadOnlyList<PendingEvent>> ReadPendingAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT position, event_type, payload::text
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
            pending.Add(new PendingEvent(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new AccountIdJsonConverter());
        options.Converters.Add(new OrderIdJsonConverter());
        return options;
    }

    private static async Task<Result<LoadedAccount, IAccountError>> LoadAccountAsync(
        NpgsqlConnection connection,
        AccountId id,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadSnapshotAsync(connection, id, cancellationToken);
        var eventsResult = await LoadEventsAsync(connection, id, snapshot?.Position ?? 0, cancellationToken);
        if (eventsResult.IsError)
            return Result<LoadedAccount, IAccountError>.Error(eventsResult.ErrorValue);
        var events = eventsResult.ResultValue;
        if (snapshot is null && events.Count == 0)
            return Result<LoadedAccount, IAccountError>.Error(new AccountNotFound());

        var accountResult = snapshot is null
            ? Domain.Account.Rehydrate(id, events)
            : Domain.Account.Rehydrate(id, snapshot.State, events);
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
            SELECT position, available_balance, provisioned_balance
            FROM account_snapshots
            WHERE account_id = @id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id.Guid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StoredSnapshot(
            reader.GetInt64(0),
            new AccountSnapshot(id, reader.GetDecimal(1), reader.GetDecimal(2)));
    }

    private static async Task<Result<List<AccountEvent>, IAccountError>> LoadEventsAsync(
        NpgsqlConnection connection,
        AccountId id,
        long afterPosition,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_type, payload::text
            FROM account_events
            WHERE account_id = @id AND position > @afterPosition
            ORDER BY position;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id.Guid);
        command.Parameters.AddWithValue("afterPosition", afterPosition);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<AccountEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(0);
            var payload = reader.GetString(1);
            AccountEvent? accountEvent;
            try
            {
                accountEvent = type switch
                {
                    nameof(AccountOpened) => JsonSerializer.Deserialize<AccountOpened>(payload, JsonOptions),
                    nameof(MoneyDeposited) => JsonSerializer.Deserialize<MoneyDeposited>(payload, JsonOptions),
                    nameof(MoneyWithdrawn) => JsonSerializer.Deserialize<MoneyWithdrawn>(payload, JsonOptions),
                    nameof(FundsReserved) => JsonSerializer.Deserialize<FundsReserved>(payload, JsonOptions),
                    nameof(FundsReleased) => JsonSerializer.Deserialize<FundsReleased>(payload, JsonOptions),
                    nameof(FundsDebited) => JsonSerializer.Deserialize<FundsDebited>(payload, JsonOptions),
                    _ => null
                };
            }
            catch (JsonException)
            {
                return Result<List<AccountEvent>, IAccountError>.Error(new CorruptAccountHistory());
            }

            if (accountEvent is null)
                return Result<List<AccountEvent>, IAccountError>.Error(new CorruptAccountHistory());
            events.Add(accountEvent);
        }

        return events;
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
        long lastPosition = 0;
        foreach (var @event in account.UncommittedEvents)
        {
            var eventType = @event.GetType().Name;
            var payload = JsonSerializer.Serialize(@event, @event.GetType(), JsonOptions);
            const string insertEventSql = """
                INSERT INTO account_events(account_id, event_type, payload)
                VALUES(@accountId, @eventType, @payload::jsonb)
                RETURNING position;
                """;
            await using var insertEvent = new NpgsqlCommand(insertEventSql, connection, transaction);
            insertEvent.Parameters.AddWithValue("accountId", account.Id.Guid);
            insertEvent.Parameters.AddWithValue("eventType", eventType);
            insertEvent.Parameters.AddWithValue("payload", payload);
            var insertedPosition = await insertEvent.ExecuteScalarAsync(cancellationToken);
            if (insertedPosition is not long position)
                return Result<bool, IAccountError>.Error(new AccountPersistenceFailure());
            lastPosition = position;

            const string insertOutboxSql = """
                INSERT INTO event_outbox(position, event_type, payload)
                VALUES(@position, @eventType, @payload::jsonb);
                """;
            await using var insertOutbox = new NpgsqlCommand(insertOutboxSql, connection, transaction);
            insertOutbox.Parameters.AddWithValue("position", lastPosition);
            insertOutbox.Parameters.AddWithValue("eventType", eventType);
            insertOutbox.Parameters.AddWithValue("payload", payload);
            await insertOutbox.ExecuteNonQueryAsync(cancellationToken);
        }

        if (loaded.EventsSinceSnapshot + account.UncommittedEvents.Count >= SnapshotInterval)
        {
            var snapshot = await UpsertSnapshotAsync(connection, transaction, account, lastPosition, cancellationToken);
            if (snapshot.IsError)
                return Result<bool, IAccountError>.Error(snapshot.ErrorValue);
        }

        return true;
    }

    private static async Task<Result<bool, IAccountError>> UpsertSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Domain.Account account,
        long position,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO account_snapshots(account_id, position, available_balance, provisioned_balance)
            VALUES(@accountId, @position, @availableBalance, @provisionedBalance)
            ON CONFLICT (account_id) DO UPDATE
            SET position = EXCLUDED.position,
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
        command.Parameters.AddWithValue("position", position);
        command.Parameters.AddWithValue("availableBalance", snapshot.AvailableBalance);
        command.Parameters.AddWithValue("provisionedBalance", snapshot.ProvisionedBalance);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task<Result<bool, IAccountError>> PersistOpeningEventAsync(
        NpgsqlConnection connection,
        Domain.Account account,
        CancellationToken cancellationToken)
    {
        var openingEvent = account.UncommittedEvents.Single();
        var eventType = openingEvent.GetType().Name;
        var payload = JsonSerializer.Serialize(openingEvent, openingEvent.GetType(), JsonOptions);
        const string sql = """
            WITH inserted_event AS (
                INSERT INTO account_events(account_id, event_type, payload)
                VALUES(@accountId, @eventType, @payload::jsonb)
                RETURNING position, event_type, payload
            ),
            inserted_outbox AS (
                INSERT INTO event_outbox(position, event_type, payload)
                SELECT position, event_type, payload
                FROM inserted_event
                RETURNING position
            )
            SELECT position FROM inserted_event;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("accountId", account.Id.Guid);
        command.Parameters.AddWithValue("eventType", eventType);
        command.Parameters.AddWithValue("payload", payload);
        var position = await command.ExecuteScalarAsync(cancellationToken);
        return position is long
            ? true
            : Result<bool, IAccountError>.Error(new AccountPersistenceFailure());
    }

    private sealed record LoadedAccount(Domain.Account Account, int EventsSinceSnapshot);

    private sealed record StoredSnapshot(long Position, AccountSnapshot State);
}
