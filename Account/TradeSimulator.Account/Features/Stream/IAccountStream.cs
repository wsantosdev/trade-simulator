using System.Text.Json;
using Moonad;
using TradeSimulator.Account.Domain;

namespace TradeSimulator.Account.Features.Stream;

public sealed record StreamEntry(
    int Version,
    string EventType,
    DateTimeOffset OccurredAt,
    JsonElement Payload,
    decimal AvailableBalance,
    decimal BlockedBalance);

// InSync tells whether the balance read model equals the balance obtained by replaying the whole stream.
public sealed record AccountStreamView(
    Guid AccountId,
    int Version,
    decimal AvailableBalance,
    decimal BlockedBalance,
    decimal ReplayedAvailableBalance,
    decimal ReplayedBlockedBalance,
    bool InSync,
    IReadOnlyList<StreamEntry> Events);

// Administrative operations over an account's event stream.
public interface IAccountStream
{
    Task<Result<AccountStreamView, IAccountError>> GetAsync(Guid accountId, CancellationToken cancellationToken);

    // Inserts a deposit/withdrawal at `position`, shifting the versions of the later events, then replays the stream.
    Task<Result<AccountStreamView, IAccountError>> InsertAsync(
        Guid accountId, int position, string type, decimal amount, CancellationToken cancellationToken);

    // Replays the whole stream and rewrites the balance read model and the snapshot from it.
    Task<Result<AccountStreamView, IAccountError>> ReplayAsync(Guid accountId, CancellationToken cancellationToken);
}
