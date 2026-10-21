using TradeSimulator.Exchange.Domain.Entities;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Processors.OfferBook;

public sealed record BookDelta(
    Ticker Ticker,
    long Sequence,
    long PreviousSequence,
    SessionPhase Phase,
    IReadOnlyList<BookChange> Changes,
    DateTimeOffset Timestamp);
