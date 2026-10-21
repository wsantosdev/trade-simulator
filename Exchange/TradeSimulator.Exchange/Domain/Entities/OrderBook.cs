using Moonad;
using TradeSimulator.Exchange.Domain.Enums;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Domain.Entities;

// Order book of one ticker: price levels with FIFO queues, price-time priority and execution at the resting order's price.
// Not thread-safe: the match engine serializes every operation.
public sealed class OrderBook(Ticker ticker)
{
    public const decimal Tick = 0.01m;

    private readonly SortedDictionary<decimal, List<BookOrder>> _bids = new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));
    private readonly SortedDictionary<decimal, List<BookOrder>> _asks = [];
    private readonly Dictionary<OrderId, BookOrder> _resting = [];

    public Ticker Ticker { get; } = ticker;

    public int RestingCount => _resting.Count;

    public static bool IsOnTick(decimal price) => price > 0 && price % Tick == 0;

    public bool HasUserOrders => _resting.Values.Any(order => order.Origin == OrderOrigin.User);

    public IReadOnlyList<BookOrder> RestingOrders() => [.. _resting.Values];

    public bool IsResting(OrderId id) => _resting.ContainsKey(id);

    public IReadOnlyList<BookOrder> OrdersAt(OrderSide side, decimal price) =>
        SideOf(side).TryGetValue(price, out var queue) ? [.. queue] : [];

    public IReadOnlyList<BookLevel> Levels(OrderSide side, int depth)
    {
        var levels = new List<BookLevel>(depth);
        foreach (var (price, queue) in SideOf(side))
        {
            if (levels.Count == depth)
                break;
            var total = queue.Aggregate(Quantity.Zero, (sum, order) => sum.Plus(order.Remaining));
            levels.Add(new BookLevel(PriceOf(price), total, queue.Count));
        }

        return levels;
    }

    public Result<SubmitOutcome, IExchangeError> Submit(BookOrder order)
    {
        if (order.Remaining.IsZero)
            return new InvalidQuantity();
        if (_resting.ContainsKey(order.Id))
            return new OrderAlreadyExists();
        if (order.Type == OrderType.Limit && (order.Price is null || !IsOnTick(order.Price.Value.Value)))
            return new PriceNotOnTick();
        if (order.Type == OrderType.Market && order.Price is not null)
            return new InvalidOrder();

        var trades = Match(order);
        var rested = false;
        var unexecuted = Quantity.Zero;
        if (!order.Remaining.IsZero)
        {
            if (order.Type == OrderType.Limit)
            {
                Rest(order);
                rested = true;
            }
            else
            {
                unexecuted = order.Remaining;
            }
        }

        return new SubmitOutcome(trades, unexecuted, rested);
    }

    public Result<BookOrder, IExchangeError> Cancel(OrderId id)
    {
        if (!_resting.TryGetValue(id, out var order))
            return new OrderNotResting();

        Remove(order);
        return order;
    }

    // The order loses its time priority; if the new price crosses the book it executes immediately.
    public Result<ReplaceOutcome, IExchangeError> Replace(OrderId id, Price newPrice)
    {
        if (!IsOnTick(newPrice.Value))
            return new PriceNotOnTick();
        if (!_resting.TryGetValue(id, out var order))
            return new OrderNotResting();

        var executedBefore = order.Executed;
        var remainingBefore = order.Remaining;
        Remove(order);
        order.Reprice(newPrice);
        var trades = Match(order);
        if (!order.Remaining.IsZero)
            Rest(order);

        return new ReplaceOutcome(order, executedBefore, remainingBefore, trades);
    }

    private List<Trade> Match(BookOrder aggressor)
    {
        var trades = new List<Trade>();
        var opposite = SideOf(aggressor.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy);
        while (!aggressor.Remaining.IsZero && opposite.Count > 0)
        {
            var (levelPrice, queue) = opposite.First();
            var crosses = aggressor.Price is null
                || (aggressor.Side == OrderSide.Buy ? levelPrice <= aggressor.Price.Value.Value : levelPrice >= aggressor.Price.Value.Value);
            if (!crosses)
                break;

            while (!aggressor.Remaining.IsZero && queue.Count > 0)
            {
                var resting = queue[0];
                var quantity = Quantity.Min(aggressor.Remaining, resting.Remaining);
                resting.Execute(quantity);
                aggressor.Execute(quantity);
                trades.Add(new Trade(resting, aggressor, PriceOf(levelPrice), quantity, resting.Remaining, aggressor.Remaining));
                if (resting.Remaining.IsZero)
                {
                    queue.RemoveAt(0);
                    _resting.Remove(resting.Id);
                }
            }

            if (queue.Count == 0)
                opposite.Remove(levelPrice);
        }

        return trades;
    }

    private void Rest(BookOrder order)
    {
        var side = SideOf(order.Side);
        var price = order.Price!.Value.Value;
        if (!side.TryGetValue(price, out var queue))
            side[price] = queue = [];
        queue.Add(order);
        _resting[order.Id] = order;
    }

    private void Remove(BookOrder order)
    {
        var side = SideOf(order.Side);
        var price = order.Price!.Value.Value;
        if (side.TryGetValue(price, out var queue))
        {
            queue.Remove(order);
            if (queue.Count == 0)
                side.Remove(price);
        }

        _resting.Remove(order.Id);
    }

    private SortedDictionary<decimal, List<BookOrder>> SideOf(OrderSide side) => side == OrderSide.Buy ? _bids : _asks;

    // Prices on the book were validated when the order was accepted, so the factory cannot fail here.
    private static Price PriceOf(decimal value) => Price.TryCreate(value).ResultValue;
}
