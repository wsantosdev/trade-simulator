namespace TradingSimulator.OfferBook.Application;

// Bids are sorted by descending price and asks by ascending price (up to 10 levels each).
public sealed record OfferBookSnapshot(
    string Ticker,
    long Sequence,
    BookPhase Phase,
    IReadOnlyList<PriceLevel> Bids,
    IReadOnlyList<PriceLevel> Asks,
    DateTimeOffset Timestamp);
