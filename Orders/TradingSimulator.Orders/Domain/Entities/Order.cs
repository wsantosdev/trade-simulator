using Moonad;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.Events;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Entities;

public sealed class Order
{
    private readonly List<OrderEvent> _uncommitted = [];
    private OrderState _restingState = OrderState.PendingNew;
    private decimal? _pendingPrice;
    private long _filledQuantity;
    private decimal _filledNotional;

    private Order(OrderSubmitted sent)
    {
        Id = sent.OrderId;
        AccountId = sent.AccountId;
        Ticker = sent.Ticker;
        Side = sent.Side;
        Type = sent.Type;
        Quantity = sent.Quantity;
        Price = sent.Price;
        RequestHash = sent.RequestHash;
        State = OrderState.PendingDispatching;
        SentAt = sent.OccurredAt;
        LastActionAt = sent.OccurredAt;
        RemainingQuantity = sent.Quantity;
        Version = 1;
    }

    public OrderId Id { get; }
    public AccountId AccountId { get; }
    public Ticker Ticker { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public long Quantity { get; }
    public decimal? Price { get; private set; }
    public long RemainingQuantity { get; private set; }
    public long ExecutedQuantity => Quantity - RemainingQuantity;
    public decimal? AveragePrice => _filledQuantity > 0 ? _filledNotional / _filledQuantity : null;
    public decimal? LastPrice { get; private set; }
    public DateTimeOffset? LastTradeAt { get; private set; }
    public int Version { get; private set; }
    public RequestHash RequestHash { get; }
    public OrderState State { get; private set; }
    public DateTimeOffset SentAt { get; }
    public DateTimeOffset LastActionAt { get; private set; }
    public IReadOnlyList<OrderEvent> UncommittedEvents => _uncommitted;

    public static Result<Order, IOrderError> Send(
        OrderId id,
        Guid accountId,
        string? ticker,
        OrderSide side,
        OrderType type,
        long quantity,
        decimal? price,
        string? requestHash,
        DateTimeOffset now)
    {
        var account = AccountId.TryCreate(accountId);
        if (account.IsError)
            return Result<Order, IOrderError>.Error(account.ErrorValue);

        var symbol = Ticker.TryCreate(ticker);
        if (symbol.IsError)
            return Result<Order, IOrderError>.Error(symbol.ErrorValue);

        if (!Enum.IsDefined(side))
            return new InvalidOrderSide();
        if (!Enum.IsDefined(type))
            return new InvalidOrderType();
        if (quantity <= 0)
            return new InvalidQuantity();
        if (type == OrderType.Limit && price is not > 0)
            return new InvalidLimitPrice();
        if (type == OrderType.Market && price is not null)
            return new PriceNotAllowedForMarketOrder();

        var hash = RequestHash.TryCreate(requestHash);
        if (hash.IsError)
            return Result<Order, IOrderError>.Error(hash.ErrorValue);

        var sent = new OrderSubmitted(id, account.ResultValue, symbol.ResultValue, side, type, quantity, price, hash.ResultValue, now);
        var order = new Order(sent);
        order._uncommitted.Add(sent);
        return order;
    }

    // A legacy history opens with OrderSent, which was both the client submission and the dispatch to the Exchange.
    public static Result<Order, IOrderError> Rehydrate(IReadOnlyList<OrderEvent> history, bool legacyOpening = false)
    {
        if (history.Count == 0 || history[0] is not OrderSubmitted sent)
            return new OrderOpeningEventMissing();

        var order = new Order(sent);
        if (legacyOpening)
            order.State = OrderState.PendingNew;
        foreach (var @event in history.Skip(1))
        {
            if (@event is OrderSubmitted)
                return new CorruptOrderHistory();
            order.Apply(@event);
        }

        return order;
    }

    public void MarkCommitted() => _uncommitted.Clear();

    public Result<Order, IOrderError> RequestCancel(DateTimeOffset now) =>
        State is OrderState.New or OrderState.PartiallyFilled
            ? Raise(new OrderCancelRequested(Id, now))
            : new InvalidStateTransition();

    public Result<Order, IOrderError> RequestReplace(decimal newPrice, DateTimeOffset now)
    {
        if (Type != OrderType.Limit)
            return new ReplaceNotAllowedForMarketOrder();
        if (newPrice <= 0)
            return new InvalidLimitPrice();
        return State is OrderState.New or OrderState.PartiallyFilled
            ? Raise(new OrderReplaceRequested(Id, newPrice, now))
            : new InvalidStateTransition();
    }

    public Result<Order, IOrderError> Ack(DateTimeOffset now) =>
        State == OrderState.PendingNew ? Raise(new OrderAcked(Id, now)) : new InvalidStateTransition();

    public Result<Order, IOrderError> Reject(DateTimeOffset now) =>
        State == OrderState.PendingNew ? Raise(new OrderRejected(Id, now)) : new InvalidStateTransition();

    // The internal validation (funds or assets) succeeded: only now the order is sent to the Exchange.
    public Result<Order, IOrderError> Dispatch(DateTimeOffset now) =>
        State == OrderState.PendingDispatching
            ? Raise(new OrderDispatched(Id, AccountId, Ticker, Side, Type, Quantity, Price, RequestHash, now))
            : new InvalidStateTransition();

    // The internal validation failed: the order never reached the Exchange, so it is rejected locally.
    public Result<Order, IOrderError> RejectSubmission(string reason, DateTimeOffset now) =>
        State == OrderState.PendingDispatching ? Raise(new OrderRejected(Id, now, reason)) : new InvalidStateTransition();

    public Result<Order, IOrderError> PartiallyFill(long executedQuantity, long remainingQuantity, decimal executionPrice, DateTimeOffset now)
    {
        if (!CanExecute)
            return new InvalidStateTransition();
        if (executionPrice <= 0)
            return new InvalidExecutionPrice();
        if (executedQuantity <= 0 || remainingQuantity <= 0 || executedQuantity + remainingQuantity != RemainingQuantity)
            return new InvalidExecutionQuantity();
        return Raise(new OrderPartiallyFilled(Id, executedQuantity, remainingQuantity, executionPrice, now));
    }

    public Result<Order, IOrderError> Fill(long executedQuantity, decimal executionPrice, DateTimeOffset now)
    {
        if (!CanExecute)
            return new InvalidStateTransition();
        if (executionPrice <= 0)
            return new InvalidExecutionPrice();
        if (executedQuantity != RemainingQuantity)
            return new InvalidExecutionQuantity();
        return Raise(new OrderFilled(Id, executedQuantity, executionPrice, now));
    }

    public Result<Order, IOrderError> Cancel(long executedQuantity, long remainingQuantity, DateTimeOffset now)
    {
        if (!CanExecute)
            return new InvalidStateTransition();
        if (executedQuantity < 0 || remainingQuantity < 0)
            return new InvalidExecutionQuantity();
        return Raise(new OrderCancelled(Id, executedQuantity, remainingQuantity, now));
    }

    public Result<Order, IOrderError> RejectCancel(DateTimeOffset now) =>
        State is OrderState.PendingCancel or OrderState.Filled
            ? Raise(new OrderCancelRejected(Id, now))
            : new InvalidStateTransition();

    public Result<Order, IOrderError> Replace(long executedQuantity, long remainingQuantity, DateTimeOffset now)
    {
        if (State != OrderState.PendingReplace)
            return new InvalidStateTransition();
        if (executedQuantity < 0 || remainingQuantity <= 0)
            return new InvalidExecutionQuantity();
        return Raise(new OrderReplaced(Id, executedQuantity, remainingQuantity, now));
    }

    public Result<Order, IOrderError> RejectReplace(DateTimeOffset now) =>
        State is OrderState.PendingReplace or OrderState.Filled
            ? Raise(new OrderReplaceRejected(Id, now))
            : new InvalidStateTransition();

    private bool CanExecute =>
        State is OrderState.New or OrderState.PartiallyFilled or OrderState.PendingCancel or OrderState.PendingReplace;

    private Result<Order, IOrderError> Raise(OrderEvent @event)
    {
        Apply(@event);
        _uncommitted.Add(@event);
        return this;
    }

    private void Apply(OrderEvent @event)
    {
        switch (@event)
        {
            case OrderDispatched:
                State = OrderState.PendingNew;
                break;
            case OrderAcked:
                State = OrderState.New;
                _restingState = OrderState.New;
                break;
            case OrderRejected:
                State = OrderState.Rejected;
                break;
            case OrderPartiallyFilled partial:
                RegisterFill(partial.FilledQuantity, partial.ExecutionPrice, partial.OccurredAt);
                RemainingQuantity = partial.RemainingQuantity;
                _restingState = OrderState.PartiallyFilled;
                if (State is OrderState.New or OrderState.PartiallyFilled)
                    State = OrderState.PartiallyFilled;
                break;
            case OrderFilled filled:
                RegisterFill(filled.FilledQuantity, filled.ExecutionPrice, filled.OccurredAt);
                RemainingQuantity = 0;
                State = OrderState.Filled;
                _restingState = OrderState.Filled;
                break;
            case OrderCancelRequested when State != OrderState.Rejected:
                _restingState = State;
                State = OrderState.PendingCancel;
                break;
            case OrderCancelled cancelled:
                RemainingQuantity = cancelled.RemainingQuantity;
                State = OrderState.Cancelled;
                break;
            case OrderCancelRejected when State == OrderState.PendingCancel:
                State = _restingState;
                break;
            case OrderReplaceRequested replaceRequested:
                _restingState = State;
                _pendingPrice = replaceRequested.NewPrice;
                State = OrderState.PendingReplace;
                break;
            case OrderReplaced replaced:
                RemainingQuantity = replaced.RemainingQuantity;
                Price = _pendingPrice ?? Price;
                _pendingPrice = null;
                State = _restingState;
                break;
            case OrderReplaceRejected when State == OrderState.PendingReplace:
                _pendingPrice = null;
                State = _restingState;
                break;
        }

        LastActionAt = @event.OccurredAt;
        Version++;
    }

    private void RegisterFill(long quantity, decimal price, DateTimeOffset at)
    {
        _filledQuantity += quantity;
        _filledNotional += quantity * price;
        LastPrice = price;
        LastTradeAt = at;
    }
}
