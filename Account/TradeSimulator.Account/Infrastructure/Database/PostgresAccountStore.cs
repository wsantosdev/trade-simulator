using System.Reflection;
using System.Text.Json;
using Moonad;
using Npgsql;
using TradeSimulator.EventSourcing;
using TradeSimulator.Account.Features.Shared;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.Events;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Infrastructure.Serialization;
using Acc = TradeSimulator.Account.Domain.Entities.Account;

namespace TradeSimulator.Account.Infrastructure.Database;

// Only appends and reads events. Each write holds a per-account advisory lock until it commits,
// so operations on the same account run one at a time.
public sealed class PostgresAccountStore(string connectionString) : IAccountStore
{
    private static readonly EventStore Events = CreateEventStore();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var schema = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TradeSimulator.Account.Infrastructure.Database.schema.sql")!;
        using var reader = new StreamReader(schema);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<Result<Acc, IAccountError>> OpenAsync(AccountId id, CancellationToken cancellationToken)
    {
        var opened = Acc.Open(id);
        if (opened.IsError)
            return opened;

        return await WriteAsync(id, account => account is null ? opened : new AccountAlreadyExists(), cancellationToken);
    }

    public Task<Result<Acc, IAccountError>> ExecuteAsync(
        AccountId id, Func<Acc, Result<Acc, IAccountError>> operation, CancellationToken cancellationToken) =>
        WriteAsync(id, account => account is null ? new AccountNotFound() : operation(account), cancellationToken);

    public async Task<Result<Acc, IAccountError>> GetAsync(AccountId id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var account = await LoadAsync(connection, id, cancellationToken);
        return account is null ? new AccountNotFound() : account;
    }

    public async Task<Result<AccountId, IAccountError>> FindByReservationAsync(ReservationId id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT stream_id FROM events WHERE event_type = 'FundsReserved' AND payload->'reservationId'->>'guid' = @id;", connection);
        command.Parameters.AddWithValue("id", id.Guid.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is Guid stream
            ? AccountId.TryCreate(stream)
            : new ReservationNotFound();
    }

    // Lock, load, decide, append, commit. Leaving without committing rolls everything back.
    private async Task<Result<Acc, IAccountError>> WriteAsync(
        AccountId id, Func<Acc?, Result<Acc, IAccountError>> decide, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id, 0));", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", id.Guid.ToString());
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var result = decide(await LoadAsync(connection, id, cancellationToken));
        if (result.IsError)
            return result;

        var account = result.ResultValue;
        var firstVersion = account.Version - account.UncommittedEvents.Count + 1;
        await Events.AppendAsync(connection, transaction, account.UncommittedEvents, firstVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        account.CommitEvents();
        return account;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<Acc?> LoadAsync(NpgsqlConnection connection, AccountId id, CancellationToken cancellationToken)
    {
        var stored = (await Events.ReadStreamAsync(connection, id.Guid, cancellationToken)).ToList();
        return stored.Count == 0 ? null : Acc.Rehydrate(id, stored.Select(entry => (AccountEvent)entry.Event));
    }

    private static EventStore CreateEventStore()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new AccountIdJsonConverter());
        json.Converters.Add(new ReservationIdJsonConverter());
        return new EventStore(new EventStoreOptions(json, [typeof(AccountEvent).Assembly]));
    }
}
