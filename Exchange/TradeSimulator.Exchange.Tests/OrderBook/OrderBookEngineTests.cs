using Microsoft.Extensions.Logging.Abstractions;
using Moonad;
using TradeSimulator.Exchange.Domain;
using TradeSimulator.Exchange.Domain.Enums;
using TradeSimulator.Exchange.Domain.Events;
using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Infrastructure.Messaging;
using TradeSimulator.Exchange.Infrastructure.Serialization;
using TradeSimulator.Exchange.Processors.OfferBook;
using TradeSimulator.Exchange.Processors.OrderCommands;
using TradeSimulator.Exchange.Processors.Quotes;
using TradeSimulator.Exchange.Simulation;

namespace TradeSimulator.Exchange.Tests.OrderBook;

public sealed class OrderBookEngineTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset OpenTime = Day.AddHours(12);
    private static readonly Ticker Petr = Ticker.TryCreate("PETR4").ResultValue;
    private static readonly Ticker Vale = Ticker.TryCreate("VALE3").ResultValue;

    private static Price P(decimal value) => Price.TryCreate(value).ResultValue;

    private static Quantity Q(long value) => Quantity.TryCreate(value).ResultValue;

    private sealed class Harness
    {
        public Clock Clock { get; }
        public FakeOrderEvents Orders { get; } = new();
        public FakeOfferBook OfferBook { get; } = new();
        public OfferBookInterestRegistry Interest { get; }
        public QuoteSimulator Quotes { get; }
        public OrderBookEngine Engine { get; }

        public Harness(DateTimeOffset now, int seed = 1)
        {
            Clock = new Clock(now);
            var parameters = SimulationParameters.TryCreate(Petr, 35m, 34.5m, 36m, 36.5m, 34m, 30m, 40m).ResultValue;
            Interest = new OfferBookInterestRegistry(Clock);
            Quotes = new QuoteSimulator(
                new FakeSource(parameters), new FakeQuotePublisher(), new QuoteInterestRegistry(Clock),
                new Random(seed), NullLogger<QuoteSimulator>.Instance);
            Engine = new OrderBookEngine(
                Quotes, Orders, OfferBook, Interest, new SimulatedLiquidity(new Random(seed + 1)), Clock,
                TimeSpan.Zero, new FakeMetrics(), NullLogger<OrderBookEngine>.Instance);
        }

        public Task Send(OrderCommand command) => Engine.HandleAsync(command, default);

        public Task Cycle() => Engine.RunCycleAsync(Clock.GetUtcNow(), default);

        public async Task<BookSnapshot> Snapshot(Ticker ticker)
        {
            OfferBook.Snapshots.Clear();
            await Engine.PublishSnapshotAsync(ticker, default);
            return OfferBook.Snapshots.Last();
        }

        public IEnumerable<OrderEvent> EventsOf(OrderId id) =>
            Orders.Events.Where(e => e.OrderId == id);

    }

    [Fact]
    public async Task Limit_buy_is_acked_before_fills_and_fills_at_resting_prices()
    {
        var h = new Harness(OpenTime);
        h.Interest.Renew([Petr]);
        await h.Cycle();
        var asks = (await h.Snapshot(Petr)).Asks;
        var id = OrderId.New();
        var quantity = asks[0].Quantity.Value + 1;

        await h.Send(new SubmitOrder(id, Petr, OrderSide.Buy, OrderType.Limit, Q(quantity), P(asks[0].Price.Value + 1m)));

        var events = h.EventsOf(id).ToList();
        Assert.IsType<OrderAcked>(events[0]);
        var partial = Assert.IsType<OrderPartiallyFilled>(events[1]);
        Assert.Equal((asks[0].Quantity.Value, 1L, asks[0].Price), (partial.Executed.Value, partial.Remaining.Value, partial.ExecutionPrice));
        Assert.Equal(asks[1].Price, Assert.IsType<OrderFilled>(events[2]).ExecutionPrice);
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task Market_order_beyond_the_book_cancels_the_balance()
    {
        var h = new Harness(OpenTime);
        h.Interest.Renew([Petr]);
        await h.Cycle();
        var id = OrderId.New();

        await h.Send(new SubmitOrder(id, Petr, OrderSide.Sell, OrderType.Market, Q(10_000_000), null));

        var events = h.EventsOf(id).ToList();
        Assert.IsType<OrderAcked>(events[0]);
        Assert.All(events.Skip(1).SkipLast(1), e => Assert.IsType<OrderPartiallyFilled>(e));
        var cancelled = Assert.IsType<OrderCancelled>(events[^1]);
        Assert.Equal(10_000_000, cancelled.Executed.Value + cancelled.Remaining.Value);
        Assert.True(cancelled.Executed.Value > 0);
    }

    [Fact]
    public async Task Cancel_and_replace_flow()
    {
        var h = new Harness(OpenTime);
        await h.Cycle();
        h.Interest.Renew([Petr]);
        var id = OrderId.New();
        await h.Send(new SubmitOrder(id, Petr, OrderSide.Buy, OrderType.Limit, Q(300), P(1m)));
        await h.Send(new ReplaceOrder(id, P(2m)));
        await h.Send(new ReplaceOrder(id, P(1000m)));
        Assert.IsType<OrderReplaced>(h.EventsOf(id).ElementAt(2));
        Assert.Contains(h.EventsOf(id).Skip(3), e => e is OrderFilled or OrderPartiallyFilled);

        var other = OrderId.New();
        await h.Send(new SubmitOrder(other, Petr, OrderSide.Buy, OrderType.Limit, Q(300), P(1m)));
        await h.Send(new CancelOrder(other));
        await h.Send(new CancelOrder(other));
        var events = h.EventsOf(other).ToList();
        var cancelled = Assert.IsType<OrderCancelled>(events[1]);
        Assert.Equal((0L, 300L), (cancelled.Executed.Value, cancelled.Remaining.Value));
        Assert.IsType<OrderCancelRejected>(events[2]);

        await h.Send(new ReplaceOrder(OrderId.New(), P(1m)));
        Assert.IsType<OrderReplaceRejected>(h.Orders.Events.Last());
    }

    [Fact]
    public async Task Rejections_outside_session_without_history_and_invalid_command()
    {
        var closed = new Harness(Day.AddHours(19));
        var a = OrderId.New();
        await closed.Send(new SubmitOrder(a, Petr, OrderSide.Buy, OrderType.Limit, Q(100), P(30m)));
        Assert.IsType<OrderRejected>(Assert.Single(closed.EventsOf(a)));

        var open = new Harness(OpenTime);
        var b = OrderId.New();
        await open.Send(new SubmitOrder(b, Vale, OrderSide.Buy, OrderType.Limit, Q(100), P(30m)));
        Assert.IsType<OrderRejected>(Assert.Single(open.EventsOf(b)));

        var c = OrderId.New();
        await open.Send(new RejectOrder(c));
        await open.Send(new SubmitOrder(OrderId.New(), Petr, OrderSide.Buy, OrderType.Limit, Q(100), null));
        Assert.IsType<OrderRejected>(Assert.Single(open.EventsOf(c)));
        Assert.IsType<OrderRejected>(open.Orders.Events.Last());

        var off = OrderId.New();
        await open.Send(new SubmitOrder(off, Petr, OrderSide.Buy, OrderType.Limit, Q(100), P(30.005m)));
        Assert.IsType<OrderRejected>(Assert.Single(open.EventsOf(off)));
    }

    [Fact]
    public async Task Duplicated_submission_is_ignored()
    {
        var h = new Harness(OpenTime);
        var id = OrderId.New();
        var command = new SubmitOrder(id, Petr, OrderSide.Buy, OrderType.Limit, Q(300), P(1m));
        await h.Send(command);
        await h.Send(command);
        await h.Send(new RejectOrder(id));
        Assert.IsType<OrderAcked>(Assert.Single(h.EventsOf(id)));
    }

    [Fact]
    public async Task Book_is_seeded_with_ten_levels_per_side_and_refilled_after_consumption()
    {
        var h = new Harness(OpenTime);
        h.Interest.Renew([Petr]);
        await h.Cycle();
        var snapshot = await h.Snapshot(Petr);
        Assert.Equal(10, snapshot.Bids.Count);
        Assert.Equal(10, snapshot.Asks.Count);
        Assert.True(snapshot.Asks[0].Price.Value > snapshot.Bids[0].Price.Value);
        Assert.All(snapshot.Bids.Concat(snapshot.Asks), level => Assert.InRange(level.Orders, 1, 3));

        await h.Send(new SubmitOrder(OrderId.New(), Petr, OrderSide.Buy, OrderType.Market, Q(10_000_000), null));
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await h.Cycle();

        var refilled = await h.Snapshot(Petr);
        Assert.Equal(10, refilled.Asks.Count);
        Assert.Equal(10, refilled.Bids.Count);
    }

    [Fact]
    public async Task Closed_book_is_frozen_and_heartbeats_with_consecutive_sequence()
    {
        var h = new Harness(Day.AddHours(19));
        h.Interest.Renew([Petr]);
        await h.Cycle();
        var frozen = await h.Snapshot(Petr);
        h.OfferBook.Snapshots.Clear();

        // The first cycle is silent: the snapshot just published already counts as activity for that second.
        for (var i = 0; i < 4; i++)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            h.Interest.Renew([Petr]);
            await h.Cycle();
        }

        Assert.Equal(3, h.OfferBook.Deltas.Count);
        Assert.All(h.OfferBook.Deltas, d => Assert.Empty(d.Changes));
        Assert.Equal(h.OfferBook.Deltas.Select(d => d.Sequence - 1), h.OfferBook.Deltas.Select(d => d.PreviousSequence));
        Assert.Equal(frozen.Bids, (await h.Snapshot(Petr)).Bids);
        Assert.Equal(frozen.Asks, (await h.Snapshot(Petr)).Asks);
    }

    [Fact]
    public async Task User_order_publishes_immediate_delta_and_open_cycle_keeps_sequence_consistent()
    {
        var h = new Harness(OpenTime);
        h.Interest.Renew([Petr]);
        await h.Cycle();
        Assert.Single(h.OfferBook.Snapshots);
        Assert.Empty(h.OfferBook.Deltas);

        await h.Send(new SubmitOrder(OrderId.New(), Petr, OrderSide.Buy, OrderType.Limit, Q(300), P(1000m)));
        var delta = Assert.Single(h.OfferBook.Deltas);
        Assert.Equal((2L, 1L), (delta.Sequence, delta.PreviousSequence));
        Assert.NotEmpty(delta.Changes);

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Interest.Renew([Petr]);
        await h.Cycle();
        var all = h.OfferBook.Deltas.ToList();
        Assert.All(all.Zip(all.Skip(1)), pair => Assert.Equal(pair.First.Sequence, pair.Second.PreviousSequence));
    }

    [Fact]
    public async Task No_publication_without_interest()
    {
        var h = new Harness(OpenTime);
        await h.Cycle();
        Assert.Empty(h.OfferBook.Snapshots);
        Assert.Empty(h.OfferBook.Deltas);
    }

    [Fact]
    public async Task Crossing_user_sell_is_filled_by_simulated_bids()
    {
        var h = new Harness(OpenTime);
        var id = OrderId.New();
        await h.Send(new SubmitOrder(id, Petr, OrderSide.Sell, OrderType.Limit, Q(100), P(1m)));
        Assert.IsType<OrderAcked>(h.EventsOf(id).First());
        Assert.Contains(h.EventsOf(id), e => e is OrderFilled);
    }

    [Fact]
    public async Task Quote_is_cached_for_the_same_second()
    {
        var h = new Harness(OpenTime);
        var first = await h.Quotes.NextQuoteAsync(Petr, OpenTime, default);
        var again = await h.Quotes.NextQuoteAsync(Petr, OpenTime, default);
        Assert.Same(first, again);
        var next = await h.Quotes.NextQuoteAsync(Petr, OpenTime.AddSeconds(1), default);
        Assert.NotSame(first, next);
    }

    [Fact]
    public void Simulated_liquidity_replaces_orders_outside_the_grid_and_is_deterministic()
    {
        var book = new Exchange.Domain.Entities.OrderBook(Petr);
        var liquidity = new SimulatedLiquidity(new Random(3));
        liquidity.Maintain(book, 35m, perturb: false);
        liquidity.Maintain(book, 36m, perturb: false);
        var (bid, ask) = SimulatedLiquidity.Grid(36m);
        Assert.Equal(bid, book.Levels(OrderSide.Buy, 10)[0].Price.Value);
        Assert.Equal(ask, book.Levels(OrderSide.Sell, 10)[0].Price.Value);
        Assert.Equal(10, book.Levels(OrderSide.Buy, 20).Count);
        Assert.Equal(10, book.Levels(OrderSide.Sell, 20).Count);
        Assert.All(book.RestingOrders(), o => Assert.Equal(OrderOrigin.Simulated, o.Origin));
    }

    private static BookLevel Level(decimal price, long quantity, int orders) =>
        new(Price.TryCreate(price).ResultValue, Quantity.TryCreate(quantity).ResultValue, orders);

    [Fact]
    public void Window_diff_reports_changed_and_removed_levels()
    {
        var previous = new OfferBookWindow([Level(10m, 100, 1), Level(9.99m, 100, 1)], [Level(10.01m, 100, 1)]);
        var current = new OfferBookWindow([Level(10m, 150, 2)], [Level(10.01m, 100, 1), Level(10.02m, 50, 1)]);

        var changes = OfferBookWindow.Diff(previous, current);

        Assert.Equal(3, changes.Count);
        Assert.Contains(new BookChange(OrderSide.Buy, Level(10m, 150, 2).Price, Level(10m, 150, 2).Quantity, 2), changes);
        Assert.Contains(new BookChange(OrderSide.Buy, Level(9.99m, 1, 1).Price, Quantity.Zero, 0), changes);
        Assert.Contains(new BookChange(OrderSide.Sell, Level(10.02m, 50, 1).Price, Level(10.02m, 50, 1).Quantity, 1), changes);
        Assert.Empty(OfferBookWindow.Diff(current, current));
    }

    [Fact]
    public void Command_parser_parses_orders_envelopes()
    {
        var id = Guid.CreateVersion7();
        var body = $$$"""{"id":"{{{Guid.NewGuid()}}}","eventType":"OrderDispatched","event":{"orderId":{"guid":"{{{id}}}"},"ticker":"PETR4","side":"Buy","type":"Limit","quantity":100,"price":10.5}}""";
        var parsed = OrderCommandParser.Parse(body, MessageJson.Options);
        var submit = Assert.IsType<SubmitOrder>(parsed.ResultValue);
        Assert.Equal((id, 100L, 10.5m), (submit.OrderId.Guid, submit.Quantity.Value, submit.Price!.Value.Value));

        var cancel = $$$"""{"id":"{{{Guid.NewGuid()}}}","eventType":"OrderCancelRequested","event":{"orderId":"{{{id}}}"}}""";
        Assert.IsType<CancelOrder>(OrderCommandParser.Parse(cancel, MessageJson.Options).ResultValue);

        var invalidQuantity = body.Replace("\"quantity\":100", "\"quantity\":0");
        Assert.IsType<RejectOrder>(OrderCommandParser.Parse(invalidQuantity, MessageJson.Options).ResultValue);

        Assert.True(OrderCommandParser.Parse("not json", MessageJson.Options).IsError);
        Assert.True(OrderCommandParser.Parse(body.Replace(id.ToString(), Guid.NewGuid().ToString()), MessageJson.Options).IsError);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public void Advance(TimeSpan span) => _now += span;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeSource(SimulationParameters parameters) : IQuoteSimulationSource
    {
        public Task<Result<SimulationParameters, IExchangeError>> LoadAsync(Ticker ticker, CancellationToken ct) =>
            Task.FromResult<Result<SimulationParameters, IExchangeError>>(
                ticker == parameters.Ticker ? parameters : new NoQuoteHistory());
    }

    private sealed class FakeQuotePublisher : IQuotePublisher
    {
        public Task PublishAsync(Quote quote, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeOrderEvents : IOrderEventPublisher
    {
        public List<OrderEvent> Events { get; } = [];

        public Task PublishAsync(Guid eventId, OrderEvent orderEvent, CancellationToken ct)
        {
            Events.Add(orderEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOfferBook : IOfferBookPublisher
    {
        public List<BookSnapshot> Snapshots { get; } = [];
        public List<BookDelta> Deltas { get; } = [];

        public Task PublishSnapshotAsync(BookSnapshot snapshot, CancellationToken ct)
        {
            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task PublishDeltaAsync(BookDelta delta, CancellationToken ct)
        {
            Deltas.Add(delta);
            return Task.CompletedTask;
        }
    }
}
