using TradingSimulator.OfferBook.Domain.Enums;
using TradingSimulator.OfferBook.Domain.ValueObjects;

namespace TradingSimulator.OfferBook.Common;

// Bids are sorted by descending price and asks by ascending price (up to 10 levels each).
public sealed record OfferBookSnapshot(
    string Ticker,
    long Sequence,
    BookPhase Phase,
    IReadOnlyList<PriceLevel> Bids,
    IReadOnlyList<PriceLevel> Asks,
    DateTimeOffset Timestamp);
