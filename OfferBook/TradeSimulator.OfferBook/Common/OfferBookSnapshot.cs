using TradeSimulator.OfferBook.Domain.Enums;
using TradeSimulator.OfferBook.Domain.ValueObjects;

namespace TradeSimulator.OfferBook.Common;

// Bids are sorted by descending price and asks by ascending price (up to 10 levels each).
public sealed record OfferBookSnapshot(
    string Ticker,
    long Sequence,
    BookPhase Phase,
    IReadOnlyList<PriceLevel> Bids,
    IReadOnlyList<PriceLevel> Asks,
    DateTimeOffset Timestamp);
