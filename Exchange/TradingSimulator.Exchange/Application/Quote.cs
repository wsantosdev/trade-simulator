using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

// Open is today's opening price, Close is the previous day's closing price, High/Low are the extremes so far today,
// Last is the current price and Variation compares Last with the immediately preceding quote.
public sealed record Quote(
    string Ticker,
    decimal Open,
    decimal Close,
    decimal High,
    decimal Low,
    decimal Last,
    QuoteVariation Variation,
    DateTimeOffset Timestamp);
