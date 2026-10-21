using TradeSimulator.Quotes.Domain.ValueObjects;

namespace TradeSimulator.Quotes.Processors.QuoteInterest;

// Which connections follow which tickers, so the Exchange is told what is worth simulating.
public sealed class QuoteSubscriptions
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<Ticker>> _byConnection = [];
    private readonly Dictionary<Ticker, int> _followers = [];

    // True when this is the first subscriber of the ticker.
    public bool Subscribe(string connectionId, Ticker ticker)
    {
        lock (_lock)
        {
            if (!_byConnection.TryGetValue(connectionId, out var tickers))
                _byConnection[connectionId] = tickers = [];
            if (!tickers.Add(ticker))
                return false;

            var count = _followers.GetValueOrDefault(ticker) + 1;
            _followers[ticker] = count;
            return count == 1;
        }
    }

    public void Unsubscribe(string connectionId, Ticker ticker)
    {
        lock (_lock)
        {
            if (_byConnection.TryGetValue(connectionId, out var tickers) && tickers.Remove(ticker))
                Release(ticker);
        }
    }

    public void Disconnect(string connectionId)
    {
        lock (_lock)
        {
            if (!_byConnection.Remove(connectionId, out var tickers))
                return;
            foreach (var ticker in tickers)
                Release(ticker);
        }
    }

    public IReadOnlyList<Ticker> ActiveTickers()
    {
        lock (_lock)
            return [.. _followers.Keys];
    }

    private void Release(Ticker ticker)
    {
        if (_followers[ticker] <= 1)
            _followers.Remove(ticker);
        else
            _followers[ticker]--;
    }
}
