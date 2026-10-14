using System.Collections.Concurrent;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Common;

// The book of a ticker is published only while its interest is renewed; it lapses on its own if the OfferBook module goes away.
public sealed class OfferBookInterestRegistry(TimeProvider timeProvider)
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Ticker, DateTimeOffset> _expirations = new();

    public void Renew(IEnumerable<Ticker> tickers)
    {
        var expiration = timeProvider.GetUtcNow() + TimeToLive;
        foreach (var ticker in tickers)
            _expirations[ticker] = expiration;
    }

    public bool IsActive(Ticker ticker) =>
        _expirations.TryGetValue(ticker, out var expiration) && expiration > timeProvider.GetUtcNow();

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
