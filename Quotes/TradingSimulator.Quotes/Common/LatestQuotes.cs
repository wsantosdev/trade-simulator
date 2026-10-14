using System.Collections.Concurrent;
using TradingSimulator.Quotes.Domain.ValueObjects;

namespace TradingSimulator.Quotes.Common;

// Last quote received per ticker; delivered to a client as soon as it subscribes.
public sealed class LatestQuotes
{
    private readonly ConcurrentDictionary<string, Quote> _quotes = new();

    public void Update(Quote quote) => _quotes[quote.Ticker] = quote;

    public Quote? Find(Ticker ticker) => _quotes.GetValueOrDefault(ticker.Value);
}
