using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

// Generates the simulated offers around the last price: a grid of levels on each side with 1-3 offers per level,
// refilled when consumed and randomly perturbed so that the book keeps changing. Simulated orders go through the same
// matching as any other, so they can execute against User orders that cross them.
public sealed class SimulatedLiquidity(Random random)
{
    public const int Levels = OfferBookWindow.Depth;
    private const int MaxOrdersPerLevel = 3;
    private const decimal SpreadFraction = 0.0005m;

    public static (decimal BestBid, decimal BestAsk) Grid(decimal last)
    {
        var spreadTicks = Math.Max(1, (int)Math.Round(last * SpreadFraction / OrderBook.Tick, MidpointRounding.AwayFromZero));
        var bestBid = Math.Max(OrderBook.Tick, decimal.Round(last, 2) - spreadTicks / 2 * OrderBook.Tick);
        return (bestBid, bestBid + spreadTicks * OrderBook.Tick);
    }

    public IReadOnlyList<Trade> Maintain(OrderBook book, decimal last, bool perturb)
    {
        var trades = new List<Trade>();
        var (bestBid, bestAsk) = Grid(last);
        var lowestBid = bestBid - (Levels - 1) * OrderBook.Tick;
        var highestAsk = bestAsk + (Levels - 1) * OrderBook.Tick;

        foreach (var order in book.RestingOrders().Where(order => order.Origin == OrderOrigin.Simulated))
        {
            var price = order.Price!.Value.Value;
            var inGrid = order.Side == OrderSide.Buy
                ? price >= lowestBid && price <= bestBid
                : price >= bestAsk && price <= highestAsk;
            if (!inGrid)
                book.Cancel(order.Id);
        }

        for (var level = 0; level < Levels; level++)
        {
            Refill(book, OrderSide.Buy, bestBid - level * OrderBook.Tick, trades);
            Refill(book, OrderSide.Sell, bestAsk + level * OrderBook.Tick, trades);
        }

        if (perturb)
        {
            var changes = random.Next(1, 4);
            for (var change = 0; change < changes; change++)
            {
                var side = random.Next(2) == 0 ? OrderSide.Buy : OrderSide.Sell;
                var level = random.Next(Levels);
                var price = side == OrderSide.Buy ? bestBid - level * OrderBook.Tick : bestAsk + level * OrderBook.Tick;
                Perturb(book, side, price, trades);
            }
        }

        return trades;
    }

    private void Refill(OrderBook book, OrderSide side, decimal price, List<Trade> trades)
    {
        if (price <= 0 || book.OrdersAt(side, price).Any(order => order.Origin == OrderOrigin.Simulated))
            return;

        var count = random.Next(1, MaxOrdersPerLevel + 1);
        for (var i = 0; i < count; i++)
            Add(book, side, price, trades);
    }

    private void Perturb(OrderBook book, OrderSide side, decimal price, List<Trade> trades)
    {
        if (price <= 0)
            return;

        var simulated = book.OrdersAt(side, price).Where(order => order.Origin == OrderOrigin.Simulated).ToList();
        if (simulated.Count == 0)
        {
            Refill(book, side, price, trades);
            return;
        }

        switch (random.Next(3))
        {
            case 0 when simulated.Count < MaxOrdersPerLevel:
                Add(book, side, price, trades);
                break;
            case 1 when simulated.Count > 1:
                book.Cancel(simulated[random.Next(simulated.Count)].Id);
                break;
            default:
                // Changing the quantity is a cancel/re-insert: the offer goes to the back of the queue.
                book.Cancel(simulated[random.Next(simulated.Count)].Id);
                Add(book, side, price, trades);
                break;
        }
    }

    private void Add(OrderBook book, OrderSide side, decimal price, List<Trade> trades)
    {
        // Lots of 100, skewed towards small sizes.
        var lots = Math.Max(1, (int)Math.Ceiling(Math.Pow(random.NextDouble(), 2) * 50));
        var quantity = Quantity.TryCreate(lots * 100L).ResultValue;
        var order = BookOrder.Limit(OrderId.New(), side, Price.TryCreate(price).ResultValue, quantity, OrderOrigin.Simulated);
        var outcome = book.Submit(order);
        if (!outcome.IsError)
            trades.AddRange(outcome.ResultValue.Trades);
    }
}
