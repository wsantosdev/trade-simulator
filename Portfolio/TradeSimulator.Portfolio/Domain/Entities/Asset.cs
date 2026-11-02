using Moonad;
using TradeSimulator.EventSourcing;
using TradeSimulator.Portfolio.Domain.Events;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Entities;

public sealed class Asset : EventSourcedEntity
{
    public const long MaxOperationQuantity = 1_000_000;

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

    private readonly Dictionary<Guid, long> _remaining = [];
    private readonly Dictionary<Guid, long> _reserved = [];
    private readonly HashSet<Guid> _settlements = [];

    public Quantity Total => Available.Add(Blocked);

    public bool IsEmpty => Total.IsZero;

    public static Result<Asset, IPortfolioError> Open(
        AssetId id, AccountId accountId, Ticker ticker, Quantity quantity, DateTimeOffset now)
    {
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);

        var deposited = new AssetDeposited(id, accountId, ticker, quantity, now);
        var asset = new Asset(deposited);
        asset.RaiseEvent(deposited);
        return asset;
    }

    public static Result<Asset, IPortfolioError> Rehydrate(IReadOnlyList<AssetEvent> history)
    {
        var asset = new Asset((AssetDeposited)history[0]);
        foreach (var @event in history)
            asset.ReplayEvent(@event);
        return asset;
    }

    public Result<Asset, IPortfolioError> Deposit(Quantity quantity, DateTimeOffset now)
    {
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);
        if (!Total.CanAdd(quantity))
            return new QuantityOverflow();
        return Raise(new AssetDeposited(Id, AccountId, Ticker, quantity, now));
    }

    // Blocks the quantity under the reservation id. Repeating the same reservation changes nothing.
    public Result<Asset, IPortfolioError> Reserve(Guid reservationId, Quantity quantity, DateTimeOffset now)
    {
        if (_reserved.TryGetValue(reservationId, out var reserved))
            return reserved == quantity.Value ? this : new ReservationConflict();
        if (Validate(quantity) is { } invalid)
            return Result<Asset, IPortfolioError>.Error(invalid);
        if (quantity > Available)
            return new InsufficientAvailableQuantity();
        return Raise(new AssetBlocked(Id, quantity, now, reservationId));
    }

    // Withdraws part of a reservation (never more than what is left); when closing, the leftover is unblocked.
    // Repeating an operation, or settling a closed reservation, changes nothing.
    public Result<Asset, IPortfolioError> Settle(Guid reservationId, Guid operationId, long quantity, bool close, DateTimeOffset now)
    {
        if (_settlements.Contains(operationId))
            return this;
        if (!_remaining.TryGetValue(reservationId, out var remaining))
            return new ReservationNotFound();
        if (remaining == 0)
            return this;

        var settled = Math.Min(quantity, remaining);
        if (settled > 0)
            RaiseEvent(new AssetWithdrawn(Id, Quantity.TryCreate(settled).ResultValue, now, reservationId, operationId));

        return close || settled == remaining ? CloseReservation(reservationId, now) : this;
    }

    // Unblocks what is still held by the reservation. Closed reservations change nothing.
    public Result<Asset, IPortfolioError> CloseReservation(Guid reservationId, DateTimeOffset now)
    {
        if (!_remaining.TryGetValue(reservationId, out var remaining))
            return new ReservationNotFound();
        if (remaining > 0)
            RaiseEvent(new AssetUnblocked(Id, Quantity.TryCreate(remaining).ResultValue, now, reservationId));
        return this;
    }

    private static IPortfolioError? Validate(Quantity quantity) =>
        quantity.IsZero ? new InvalidQuantity()
        : quantity.Value > MaxOperationQuantity ? new QuantityAboveLimit()
        : null;

    private Result<Asset, IPortfolioError> Raise(AssetEvent @event)
    {
        RaiseEvent(@event);
        return this;
    }

    protected override void ProcessEvent(IEvent domainEvent)
    {
        switch (domainEvent)
        {
            case AssetDeposited deposited:
                Available = Available.Add(deposited.Quantity);
                break;
            case AssetBlocked blocked:
                Available = Available.Subtract(blocked.Quantity);
                Blocked = Blocked.Add(blocked.Quantity);
                _reserved[blocked.ReservationId] = blocked.Quantity.Value;
                _remaining[blocked.ReservationId] = blocked.Quantity.Value;
                break;
            case AssetWithdrawn withdrawn:
                Blocked = Blocked.Subtract(withdrawn.Quantity);
                _remaining[withdrawn.ReservationId] -= withdrawn.Quantity.Value;
                _settlements.Add(withdrawn.OperationId);
                break;
            case AssetUnblocked unblocked:
                Blocked = Blocked.Subtract(unblocked.Quantity);
                Available = Available.Add(unblocked.Quantity);
                _remaining[unblocked.ReservationId] -= unblocked.Quantity.Value;
                break;
        }
    }
}
