using Microsoft.Extensions.Logging;
using TradingSimulator.Exchange.Domain.Entities;
using TradingSimulator.Exchange.Domain.Enums;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Infrastructure.Messaging;

namespace TradingSimulator.Exchange.Common;

// The match engine: one order book per ticker, in memory, with every operation serialized by a single gate.
// User orders come from the Orders module; simulated liquidity is generated around the last quote. Nothing is persisted:
// after a restart the books are rebuilt from the quotes and resting User orders are lost.
public sealed class OrderBookEngine(
    QuoteSimulator quotes,
    IOrderEventPublisher orderEvents,
    IOfferBookPublisher offerBook,
    OfferBookInterestRegistry interest,
    SimulatedLiquidity liquidity,
    TimeProvider timeProvider,
    TimeSpan sessionClockOffset,
    ILogger<OrderBookEngine> logger)
{
    private static readonly TimeSpan IdleRetention = TimeSpan.FromMinutes(10);
    private const int MaxPendingEvents = 100_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Ticker, TickerBook> _books = [];
    // Every User order ever received (the key makes redelivered commands idempotent and routes cancel/replace).
    private readonly Dictionary<OrderId, Ticker> _userOrders = [];
    // Events for the Orders module wait here until published, so a broker failure never loses them or reorders them.
    private readonly Queue<EventEnvelope<object>> _pendingEvents = new();

    public Task HandleAsync(OrderCommand command, CancellationToken cancellationToken) =>
        SerializedAsync(async () =>
        {
            switch (command)
            {
                case SubmitOrder submit:
                    await SubmitAsync(submit, cancellationToken);
                    break;
                case RejectOrder reject:
                    if (_userOrders.TryAdd(reject.OrderId, default))
                        Enqueue(new OrderRejectedMessage(reject.OrderId));
                    break;
                case CancelOrder cancel:
                    await CancelAsync(cancel, cancellationToken);
                    break;
                case ReplaceOrder replace:
                    await ReplaceAsync(replace, cancellationToken);
                    break;
            }
        }, cancellationToken);

    public Task RunCycleAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        SerializedAsync(async () =>
        {
            var tickers = interest.ActiveTickers()
                .Concat(_books.Where(entry => entry.Value.Book.HasUserOrders).Select(entry => entry.Key))
                .Distinct();
            foreach (var ticker in tickers)
            {
                try
                {
                    await CycleAsync(ticker, now, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Order book cycle failed for {Ticker}.", ticker);
                }
            }

            Prune(now);
        }, cancellationToken);

    // Publishes the current book of the ticker as a snapshot right away (a subscriber asked for it).
    public Task PublishSnapshotAsync(Ticker ticker, CancellationToken cancellationToken) =>
        SerializedAsync(async () =>
        {
            var now = Now();
            var position = TradingSession.At(now);
            var quote = await quotes.NextQuoteAsync(ticker, now, cancellationToken);
            if (quote is null && !_books.ContainsKey(ticker))
                return;

            var book = quote is null ? _books[ticker] : BookFor(ticker, quote, now);
            await PublishSnapshotAsync(book, position.Phase, now, cancellationToken);
        }, cancellationToken);

    private async Task SubmitAsync(SubmitOrder command, CancellationToken cancellationToken)
    {
        if (!_userOrders.TryAdd(command.OrderId, command.Ticker))
        {
            logger.LogInformation("Ignored duplicated submission of order {OrderId}.", command.OrderId);
            return;
        }

        var now = Now();
        var position = TradingSession.At(now);
        if (position.Phase != SessionPhase.Open)
        {
            Enqueue(new OrderRejectedMessage(command.OrderId));
            return;
        }

        var quote = await quotes.NextQuoteAsync(command.Ticker, now, cancellationToken);
        if (quote is null)
        {
            Enqueue(new OrderRejectedMessage(command.OrderId));
            return;
        }

        var tickerBook = BookFor(command.Ticker, quote, now);
        var order = command.Type == OrderType.Market
            ? BookOrder.Market(command.OrderId, command.Side, command.Quantity, OrderOrigin.User)
            : command.Price is { } price
                ? BookOrder.Limit(command.OrderId, command.Side, price, command.Quantity, OrderOrigin.User)
                : null;
        if (order is null)
        {
            Enqueue(new OrderRejectedMessage(command.OrderId));
            return;
        }

        var outcome = tickerBook.Book.Submit(order);
        if (outcome.IsError)
        {
            Enqueue(new OrderRejectedMessage(command.OrderId));
            return;
        }

        Enqueue(new OrderAckedMessage(command.OrderId));
        EmitTrades(outcome.ResultValue.Trades);
        if (!outcome.ResultValue.Unexecuted.IsZero)
            Enqueue(new OrderCancelledMessage(command.OrderId, order.Executed.Value, outcome.ResultValue.Unexecuted.Value));

        await PublishChangeAsync(tickerBook, position.Phase, now, heartbeat: false, cancellationToken);
    }

    private async Task CancelAsync(CancelOrder command, CancellationToken cancellationToken)
    {
        if (!_userOrders.TryGetValue(command.OrderId, out var ticker) || !_books.TryGetValue(ticker, out var tickerBook))
        {
            Enqueue(new OrderCancelRejectedMessage(command.OrderId));
            return;
        }

        var cancelled = tickerBook.Book.Cancel(command.OrderId);
        if (cancelled.IsError)
        {
            Enqueue(new OrderCancelRejectedMessage(command.OrderId));
            return;
        }

        var order = cancelled.ResultValue;
        Enqueue(new OrderCancelledMessage(order.Id, order.Executed.Value, order.Remaining.Value));
        var now = Now();
        await PublishChangeAsync(tickerBook, TradingSession.At(now).Phase, now, heartbeat: false, cancellationToken);
    }

    private async Task ReplaceAsync(ReplaceOrder command, CancellationToken cancellationToken)
    {
        var now = Now();
        var position = TradingSession.At(now);
        if (position.Phase != SessionPhase.Open
            || !_userOrders.TryGetValue(command.OrderId, out var ticker)
            || !_books.TryGetValue(ticker, out var tickerBook))
        {
            Enqueue(new OrderReplaceRejectedMessage(command.OrderId));
            return;
        }

        var replaced = tickerBook.Book.Replace(command.OrderId, command.NewPrice);
        if (replaced.IsError)
        {
            Enqueue(new OrderReplaceRejectedMessage(command.OrderId));
            return;
        }

        var outcome = replaced.ResultValue;
        Enqueue(new OrderReplacedMessage(command.OrderId, outcome.ExecutedBefore.Value, outcome.RemainingBefore.Value));
        EmitTrades(outcome.Trades);
        await PublishChangeAsync(tickerBook, position.Phase, now, heartbeat: false, cancellationToken);
    }

    private async Task CycleAsync(Ticker ticker, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var position = TradingSession.At(now);
        var quote = await quotes.NextQuoteAsync(ticker, now, cancellationToken);
        if (quote is null)
            return;

        var tickerBook = BookFor(ticker, quote, now);
        tickerBook.LastUsed = now;
        // Outside the session the book is frozen in its last state; it only moves while the session is open.
        if (position.Phase == SessionPhase.Open)
            EmitTrades(liquidity.Maintain(tickerBook.Book, quote.Last, perturb: true).ToList());

        if (interest.IsActive(ticker))
            await PublishChangeAsync(tickerBook, position.Phase, now, heartbeat: !tickerBook.PublishedSinceCycle, cancellationToken);
        tickerBook.PublishedSinceCycle = false;
    }

    // A new book is seeded around the last price of the quote (static unless the session is open).
    private TickerBook BookFor(Ticker ticker, Quote quote, DateTimeOffset now)
    {
        if (_books.TryGetValue(ticker, out var existing))
            return existing;

        var created = new TickerBook(ticker) { LastUsed = now };
        _books[ticker] = created;
        EmitTrades(liquidity.Maintain(created.Book, quote.Last, perturb: false));
        return created;
    }

    private void EmitTrades(IReadOnlyList<Trade> trades)
    {
        foreach (var trade in trades)
        {
            // Simulated orders have no counterpart in the Orders module.
            EmitFill(trade.Resting, trade.Quantity, trade.RestingRemaining, trade.Price);
            EmitFill(trade.Aggressor, trade.Quantity, trade.AggressorRemaining, trade.Price);
        }
    }

    private void EmitFill(BookOrder order, Quantity executed, Quantity remaining, Price price)
    {
        if (order.Origin != OrderOrigin.User)
            return;

        if (remaining.IsZero)
            Enqueue(new OrderFilledMessage(order.Id, executed.Value, price.Value));
        else
            Enqueue(new OrderPartiallyFilledMessage(order.Id, executed.Value, remaining.Value, price.Value));
    }

    private async Task PublishChangeAsync(
        TickerBook tickerBook, SessionPhase phase, DateTimeOffset now, bool heartbeat, CancellationToken cancellationToken)
    {
        if (!interest.IsActive(tickerBook.Ticker))
            return;

        var window = OfferBookWindow.Of(tickerBook.Book);
        if (tickerBook.Published is null)
        {
            await PublishSnapshotAsync(tickerBook, phase, now, cancellationToken);
            return;
        }

        var changes = OfferBookWindow.Diff(tickerBook.Published, window);
        if (changes.Count == 0 && !heartbeat)
            return;

        try
        {
            var delta = new OfferBookDelta(
                tickerBook.Ticker.Value, tickerBook.Sequence + 1, tickerBook.Sequence, phase, changes, now.ToOffset(TradingSession.Offset));
            await offerBook.PublishDeltaAsync(EventEnvelope.Of(delta), cancellationToken);
            tickerBook.Sequence++;
            tickerBook.Published = window;
            tickerBook.PublishedSinceCycle = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing is recorded as published: the next diff covers this change and a gap makes the consumer ask for a snapshot.
            logger.LogWarning(exception, "Could not publish the order book delta of {Ticker}.", tickerBook.Ticker);
        }
    }

    private async Task PublishSnapshotAsync(
        TickerBook tickerBook, SessionPhase phase, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var window = OfferBookWindow.Of(tickerBook.Book);
        try
        {
            var snapshot = new OfferBookSnapshot(
                tickerBook.Ticker.Value, tickerBook.Sequence + 1, phase, window.Bids, window.Asks, now.ToOffset(TradingSession.Offset));
            await offerBook.PublishSnapshotAsync(EventEnvelope.Of(snapshot), cancellationToken);
            tickerBook.Sequence++;
            tickerBook.Published = window;
            tickerBook.PublishedSinceCycle = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not publish the order book snapshot of {Ticker}.", tickerBook.Ticker);
        }
    }

    // Books with User orders are never dropped; idle books without them are rebuilt from the quotes when needed again.
    private void Prune(DateTimeOffset now)
    {
        foreach (var (ticker, tickerBook) in _books.ToList())
        {
            if (now - tickerBook.LastUsed > IdleRetention && !tickerBook.Book.HasUserOrders && !interest.IsActive(ticker))
                _books.Remove(ticker);
        }
    }

    private void Enqueue<T>(T message) where T : notnull
    {
        if (_pendingEvents.Count >= MaxPendingEvents)
        {
            logger.LogError("Order event backlog is full; dropping {EventType}.", typeof(T).Name);
            return;
        }

        _pendingEvents.Enqueue(new EventEnvelope<object>(Guid.CreateVersion7(), EventTypeOf(typeof(T)), message));
    }

    private static string EventTypeOf(Type message) => message.Name.Replace("Message", string.Empty, StringComparison.Ordinal);

    private async Task FlushEventsAsync(CancellationToken cancellationToken)
    {
        while (_pendingEvents.TryPeek(out var envelope))
        {
            try
            {
                await orderEvents.PublishAsync(envelope, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not publish {EventType}; it will be retried.", envelope.EventType);
                return;
            }

            _pendingEvents.Dequeue();
        }
    }

    private async Task SerializedAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await operation();
            await FlushEventsAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private DateTimeOffset Now() => timeProvider.GetUtcNow() + sessionClockOffset;

    private sealed class TickerBook(Ticker ticker)
    {
        public Ticker Ticker { get; } = ticker;
        public OrderBook Book { get; } = new(ticker);
        public long Sequence { get; set; }
        public OfferBookWindow? Published { get; set; }
        public bool PublishedSinceCycle { get; set; }
        public DateTimeOffset LastUsed { get; set; }
    }
}
