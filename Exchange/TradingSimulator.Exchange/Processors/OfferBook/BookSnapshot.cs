using TradingSimulator.Exchange.Domain.Entities;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Processors.OfferBook;

public sealed record BookSnapshot(
    Ticker Ticker,
    long Sequence,
    SessionPhase Phase,
    IReadOnlyList<BookLevel> Bids,
    IReadOnlyList<BookLevel> Asks,
    DateTimeOffset Timestamp);
