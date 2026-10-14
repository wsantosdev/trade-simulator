using TradingSimulator.Exchange.Domain.Entities;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Processors.OfferBook;

public sealed record BookDelta(
    Ticker Ticker,
    long Sequence,
    long PreviousSequence,
    SessionPhase Phase,
    IReadOnlyList<BookChange> Changes,
    DateTimeOffset Timestamp);
