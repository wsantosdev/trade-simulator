using System.Collections.Concurrent;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Processors.Quotes;

// A ticker is simulated only while its interest is renewed; it lapses on its own if the Quotes module goes away.
public sealed class QuoteInterestRegistry(TimeProvider timeProvider)
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Ticker, DateTimeOffset> _expirations = new();

    public void Renew(IEnumerable<Ticker> tickers)
    {
        var expiration = timeProvider.GetUtcNow() + TimeToLive;
        foreach (var ticker in tickers)
            _expirations[ticker] = expiration;
    }

    public IReadOnlyList<Ticker> ActiveTickers()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (ticker, expiration) in _expirations)
        {
            if (expiration <= now)
                _expirations.TryRemove(ticker, out _);
        }

        return [.. _expirations.Keys];
    }
}
