using Moonad;
using TradingSimulator.Portfolio.Domain.Features.Blocks;
using TradingSimulator.Portfolio.Domain.Features.Deposits;
using TradingSimulator.Portfolio.Domain.Features.Unblocks;
using TradingSimulator.Portfolio.Domain.Features.Withdrawals;

namespace TradingSimulator.Portfolio.Domain;

public sealed class Asset
{
    public const long MaxOperationQuantity = 1_000_000;

    private readonly List<AssetEvent> _uncommitted = [];

    private Asset(AssetDeposited opened) : this(opened.AssetId, opened.AccountId, opened.Ticker)
    {
    }

    private Asset(AssetId id, AccountId accountId, Ticker ticker)
    {
        Id = id;
        AccountId = accountId;
        Ticker = ticker;
    }

    public AssetId Id { get; }
    public AccountId AccountId { get; }
    public Ticker Ticker { get; }
    public Quantity Available { get; private set; }
    public Quantity Blocked { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<AssetEvent> UncommittedEvents => _uncommitted;

    public Quantity Total => Available.Add(Blocked);

    public bool IsRemoved => Total.IsZero;

    public static Result<Asset, IPortfolioError> Open(
        AssetId id, AccountId accountId, Ticker ticker, Quantity quantity, DateTimeOffset now)
    {
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);

        var deposited = new AssetDeposited(id, accountId, ticker, quantity, now);
        var asset = new Asset(deposited);
        asset.Apply(deposited);
        asset._uncommitted.Add(deposited);
        return asset;
    }

    public static Result<Asset, IPortfolioError> Rehydrate(IReadOnlyList<AssetEvent> history)
    {
        if (history.Count == 0 || history[0] is not AssetDeposited opened)
            return new AssetOpeningEventMissing();

        var asset = new Asset(opened);
        foreach (var @event in history)
        {
            if (@event.AssetId != asset.Id)
                return new CorruptAssetHistory();
            asset.Apply(@event);
        }

        return asset;
    }

    public static Result<Asset, IPortfolioError> Restore(AssetSnapshot snapshot, IReadOnlyList<AssetEvent> later)
    {
        if (snapshot.Available < 0 || snapshot.Blocked < 0 || snapshot.Version <= 0)
            return new CorruptAssetHistory();

        var asset = new Asset(snapshot.AssetId, snapshot.AccountId, snapshot.Ticker)
        {
            Available = Quantity.Restore(snapshot.Available),
            Blocked = Quantity.Restore(snapshot.Blocked),
            Version = snapshot.Version
        };

        foreach (var @event in later)
        {
            if (@event.AssetId != asset.Id)
                return new CorruptAssetHistory();
            asset.Apply(@event);
        }

        return asset;
    }

    public AssetSnapshot ToSnapshot() => new(Id, AccountId, Ticker, Available.Value, Blocked.Value, Version);

    public void MarkCommitted() => _uncommitted.Clear();

    public Result<Asset, IPortfolioError> Deposit(Quantity quantity, DateTimeOffset now)
    {
        if (IsRemoved)
            return new AssetNotFound();
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);
        if (!Total.CanAdd(quantity))
            return new QuantityOverflow();
        return Raise(new AssetDeposited(Id, AccountId, Ticker, quantity, now));
    }

    public Result<Asset, IPortfolioError> Block(Quantity quantity, DateTimeOffset now)
    {
        if (IsRemoved)
            return new AssetNotFound();
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);
        if (quantity > Available)
            return new InsufficientAvailableQuantity();
        return Raise(new AssetBlocked(Id, quantity, now));
    }

    public Result<Asset, IPortfolioError> Withdraw(Quantity quantity, DateTimeOffset now)
    {
        if (IsRemoved)
            return new AssetNotFound();
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);
        if (quantity > Blocked)
            return new InsufficientBlockedQuantity();
        return Raise(new AssetWithdrawn(Id, quantity, now));
    }

    public Result<Asset, IPortfolioError> Unblock(Quantity quantity, DateTimeOffset now)
    {
        if (IsRemoved)
            return new AssetNotFound();
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);
        if (quantity > Blocked)
            return new InsufficientBlockedQuantity();
        return Raise(new AssetUnblocked(Id, quantity, now));
    }

    private static IPortfolioError? Validate(Quantity quantity) =>
        quantity.IsZero ? new InvalidQuantity()
        : quantity.Value > MaxOperationQuantity ? new QuantityAboveLimit()
        : null;

    private Result<Asset, IPortfolioError> Raise(AssetEvent @event)
    {
        Apply(@event);
        _uncommitted.Add(@event);
        return this;
    }

    private void Apply(AssetEvent @event)
    {
        switch (@event)
        {
            case AssetDeposited deposited:
                Available = Available.Add(deposited.Quantity);
                break;
            case AssetBlocked blocked:
                Available = Available.Subtract(blocked.Quantity);
                Blocked = Blocked.Add(blocked.Quantity);
                break;
            case AssetWithdrawn withdrawn:
                Blocked = Blocked.Subtract(withdrawn.Quantity);
                break;
            case AssetUnblocked unblocked:
                Blocked = Blocked.Subtract(unblocked.Quantity);
                Available = Available.Add(unblocked.Quantity);
                break;
        }

        Version++;
    }
}
