using System.Text.Json;
using Moonad;
using Npgsql;
using TradeSimulator.Account.Domain;
using TradeSimulator.Account.Domain.Events;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Features.Stream;
using TradeSimulator.EventSourcing;

namespace TradeSimulator.Account.Infrastructure.Database;

public sealed partial class PostgresAccountStore : IAccountStream
{
    public async Task<Result<AccountStreamView, IAccountError>> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await BuildViewAsync(connection, accountId, cancellationToken);
    }

    public async Task<Result<AccountStreamView, IAccountError>> InsertAsync(
        Guid accountId, int position, string type, decimal amount, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<AccountStreamView, IAccountError>.Error(id.ErrorValue);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var stored = await ReadStoredAsync(connection, id.ResultValue, cancellationToken);
            if (stored.IsError)
                return Result<AccountStreamView, IAccountError>.Error(stored.ErrorValue);
            var events = stored.ResultValue.Select(e => (AccountEvent)e.Event).ToList();

            if (position < 2 || position > events.Count + 1)
                return Result<AccountStreamView, IAccountError>.Error(new InvalidStreamPosition());
            AccountEvent inserted = type.ToLowerInvariant() switch
            {
                "deposit" => new MoneyDeposited(id.ResultValue, amount, DateTimeOffset.UtcNow),
                "withdrawal" => new MoneyWithdrawn(id.ResultValue, amount, DateTimeOffset.UtcNow),
                _ => null!
            };
            if (inserted is null)
                return Result<AccountStreamView, IAccountError>.Error(new UnsupportedStreamEvent());

            events.Insert(position - 1, inserted);
            var replayed = Replay(id.ResultValue, events);
            if (replayed.IsError)
                return Result<AccountStreamView, IAccountError>.Error(replayed.ErrorValue);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            // Two steps (negative, then positive) so the unique (stream_id, version) index never sees a clash.
            await ExecuteAsync(connection, transaction,
                "UPDATE events SET version = -(version + 1) WHERE stream_id = @id AND version >= @position;",
                id.ResultValue.Guid, position, cancellationToken);
            await ExecuteAsync(connection, transaction,
                "UPDATE events SET version = -version WHERE stream_id = @id AND version < 0;",
                id.ResultValue.Guid, position, cancellationToken);
            await Events.AppendAsync(connection, transaction, [inserted], position, cancellationToken);
            await RebuildReadModelAsync(connection, transaction, replayed.ResultValue, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await InvalidateGateAsync(id.ResultValue, cancellationToken);
        return await GetAsync(accountId, cancellationToken);
    }

    public async Task<Result<AccountStreamView, IAccountError>> ReplayAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<AccountStreamView, IAccountError>.Error(id.ErrorValue);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var stored = await ReadStoredAsync(connection, id.ResultValue, cancellationToken);
            if (stored.IsError)
                return Result<AccountStreamView, IAccountError>.Error(stored.ErrorValue);
            var replayed = Replay(id.ResultValue, stored.ResultValue.Select(e => (AccountEvent)e.Event).ToList());
            if (replayed.IsError)
                return Result<AccountStreamView, IAccountError>.Error(replayed.ErrorValue);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await RebuildReadModelAsync(connection, transaction, replayed.ResultValue, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        await InvalidateGateAsync(id.ResultValue, cancellationToken);
        return await GetAsync(accountId, cancellationToken);
    }

    private async Task InvalidateGateAsync(AccountId id, CancellationToken cancellationToken)
    {
        if (fundsGate is not null)
            await fundsGate.InvalidateAsync(id, cancellationToken);
    }

    private static async Task<Result<IReadOnlyList<StoredEvent>, IAccountError>> ReadStoredAsync(
        NpgsqlConnection connection, AccountId id, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await Events.ReadStreamAsync(connection, id.Guid, cancellationToken);
            return stored.Count == 0
                ? Result<IReadOnlyList<StoredEvent>, IAccountError>.Error(new AccountNotFound())
                : Result<IReadOnlyList<StoredEvent>, IAccountError>.Ok(stored);
        }
        catch (CorruptEventStreamException)
        {
            return Result<IReadOnlyList<StoredEvent>, IAccountError>.Error(new CorruptAccountHistory());
        }
    }

    // Applies every event in order; the first one that cannot be applied rejects the whole replay.
    private static Result<Domain.Entities.Account, IAccountError> Replay(AccountId id, IReadOnlyList<AccountEvent> events)
    {
        var first = Domain.Entities.Account.Rehydrate(id, [events[0]]);
        if (first.IsError)
            return Result<Domain.Entities.Account, IAccountError>.Error(new StreamReplayRejected(1, events[0].GetType().Name));
        var account = first.ResultValue;
        for (var i = 1; i < events.Count; i++)
        {
            if (account.Apply(events[i]).IsError)
                return Result<Domain.Entities.Account, IAccountError>.Error(new StreamReplayRejected(i + 1, events[i].GetType().Name));
        }

        return account;
    }

    private static async Task RebuildReadModelAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Domain.Entities.Account account, CancellationToken cancellationToken)
    {
        await UpsertBalanceAsync(connection, transaction, account, cancellationToken);
        await ExecuteAsync(connection, transaction, "DELETE FROM snapshots WHERE stream_id = @id;", account.Id.Guid, 0, cancellationToken);
        if (account.Version >= SnapshotInterval)
            await UpsertSnapshotAsync(connection, transaction, account, cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid streamId, int position, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", streamId);
        if (sql.Contains("@position"))
            command.Parameters.AddWithValue("position", position);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Result<AccountStreamView, IAccountError>> BuildViewAsync(
        NpgsqlConnection connection, Guid accountId, CancellationToken cancellationToken)
    {
        var id = AccountId.TryCreate(accountId);
        if (id.IsError)
            return Result<AccountStreamView, IAccountError>.Error(id.ErrorValue);
        var stored = await ReadStoredAsync(connection, id.ResultValue, cancellationToken);
        if (stored.IsError)
            return Result<AccountStreamView, IAccountError>.Error(stored.ErrorValue);

        var entries = new List<StreamEntry>();
        var available = 0m;
        var blocked = 0m;
        Domain.Entities.Account? account = null;
        foreach (var item in stored.ResultValue)
        {
            var @event = (AccountEvent)item.Event;
            if (account is null)
            {
                var opened = Domain.Entities.Account.Rehydrate(id.ResultValue, [@event]);
                account = opened.IsOk ? opened.ResultValue : null;
            }
            else
            {
                account.Apply(@event);
            }

            available = account?.AvailableBalance ?? available;
            blocked = account?.ProvisionedBalance ?? blocked;
            using var payload = JsonDocument.Parse(Events.Serialize((IEvent)@event));
            entries.Add(new StreamEntry(item.Version, item.Type, @event.OccurredAt, payload.RootElement.Clone(), available, blocked));
        }

        const string sql = "SELECT version, available_balance, blocked_balance FROM account_balances WHERE account_id = @id;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var (version, readAvailable, readBlocked) = await reader.ReadAsync(cancellationToken)
            ? (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2))
            : (0, 0m, 0m);

        return new AccountStreamView(
            accountId, version, readAvailable, readBlocked, available, blocked,
            version == entries.Count && readAvailable == available && readBlocked == blocked, entries);
    }
}
