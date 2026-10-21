using Moonad;

namespace TradeSimulator.Exchange.Domain.ValueObjects;

// Daily OHLC of a ticker; the invariants (positive prices, Low <= Open/Close <= High) are checked at creation.
public sealed record DailyQuote
{
    public Ticker Ticker { get; }
    public DateOnly TradeDate { get; }
    public Price Open { get; }
    public Price High { get; }
    public Price Low { get; }
    public Price Close { get; }

    private DailyQuote(Ticker ticker, DateOnly tradeDate, Price open, Price high, Price low, Price close)
    {
        Ticker = ticker;
        TradeDate = tradeDate;
        Open = open;
        High = high;
        Low = low;
        Close = close;
    }

    public static Result<DailyQuote, IExchangeError> TryCreate(
        string? ticker, DateOnly tradeDate, decimal open, decimal high, decimal low, decimal close)
    {
        var parsedTicker = Ticker.TryCreate(ticker);
        if (parsedTicker.IsError)
            return new InvalidTicker();

        var prices = new[] { open, high, low, close }.Select(Price.TryCreate).ToArray();
        if (prices.Any(p => p.IsError))
            return new InvalidPrice();

        var (o, h, l, c) = (prices[0].ResultValue, prices[1].ResultValue, prices[2].ResultValue, prices[3].ResultValue);
        if (l.Value > h.Value || o.Value < l.Value || o.Value > h.Value || c.Value < l.Value || c.Value > h.Value)
            return new InvalidQuote();

        return new DailyQuote(parsedTicker.ResultValue, tradeDate, o, h, l, c);
    }
}
