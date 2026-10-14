using TradingSimulator.OfferBook.Domain;

namespace TradingSimulator.OfferBook.Application;

// In-memory book per ticker: a snapshot replaces the base and contiguous deltas are applied on top of it.
public sealed class LatestOfferBooks(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MinimumSnapshotRequestInterval = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Book> _books = [];
    private readonly Dictionary<string, DateTimeOffset> _lastRequest = [];

    public void ApplySnapshot(OfferBookSnapshot snapshot)
    {
        var book = new Book { Sequence = snapshot.Sequence, Phase = snapshot.Phase, Timestamp = snapshot.Timestamp };
        foreach (var level in snapshot.Bids.Where(level => level.Quantity > 0))
            book.Bids[level.Price] = level;
        foreach (var level in snapshot.Asks.Where(level => level.Quantity > 0))
            book.Asks[level.Price] = level;

        lock (_lock)
            _books[snapshot.Ticker] = book;
    }

    public DeltaOutcome ApplyDelta(OfferBookDelta delta)
    {
        lock (_lock)
        {
            if (!_books.TryGetValue(delta.Ticker, out var book))
                return RequestSnapshotThrottled(delta.Ticker);

            if (delta.Sequence <= book.Sequence)
                return DeltaOutcome.Ignored;

            if (delta.PreviousSequence != book.Sequence)
            {
                _books.Remove(delta.Ticker);
                return RequestSnapshotNow(delta.Ticker);
            }

            foreach (var change in delta.Changes)
            {
                var side = change.Side == BookSide.Bid ? book.Bids : book.Asks;
                if (change.Quantity <= 0)
                    side.Remove(change.Price);
                else
                    side[change.Price] = new PriceLevel(change.Price, change.Quantity, change.Orders);
            }

            book.Sequence = delta.Sequence;
            book.Phase = delta.Phase;
            book.Timestamp = delta.Timestamp;
            return DeltaOutcome.Applied;
        }
    }

    public OfferBookSnapshot? Find(Ticker ticker)
    {
        lock (_lock)
        {
            if (!_books.TryGetValue(ticker.Value, out var book))
                return null;

            return new OfferBookSnapshot(
                ticker.Value,
                book.Sequence,
                book.Phase,
                [.. book.Bids.Values.OrderByDescending(level => level.Price)],
                [.. book.Asks.Values.OrderBy(level => level.Price)],
                book.Timestamp);
        }
    }

    private DeltaOutcome RequestSnapshotThrottled(string ticker)
    {
        if (_lastRequest.TryGetValue(ticker, out var last) && _time.GetUtcNow() - last < MinimumSnapshotRequestInterval)
            return DeltaOutcome.Ignored;
        return RequestSnapshotNow(ticker);
    }

    private DeltaOutcome RequestSnapshotNow(string ticker)
    {
        _lastRequest[ticker] = _time.GetUtcNow();
        return DeltaOutcome.SnapshotNeeded;
    }

    private sealed class Book
    {
        public long Sequence;
        public BookPhase Phase;
        public DateTimeOffset Timestamp;
        public Dictionary<decimal, PriceLevel> Bids { get; } = [];
        public Dictionary<decimal, PriceLevel> Asks { get; } = [];
    }
}
