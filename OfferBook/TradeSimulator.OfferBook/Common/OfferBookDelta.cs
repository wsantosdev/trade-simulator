using TradeSimulator.OfferBook.Domain.Enums;
using TradeSimulator.OfferBook.Domain.ValueObjects;

namespace TradeSimulator.OfferBook.Common;

// Empty Changes is the 1 s heartbeat of the Exchange.
public sealed record OfferBookDelta(
    string Ticker,
    long Sequence,
    long PreviousSequence,
    BookPhase Phase,
    IReadOnlyList<LevelChange> Changes,
    DateTimeOffset Timestamp);
