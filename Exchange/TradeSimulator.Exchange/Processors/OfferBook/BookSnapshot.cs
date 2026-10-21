using TradeSimulator.Exchange.Domain.Entities;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Processors.OfferBook;

public sealed record BookSnapshot(
    Ticker Ticker,
    long Sequence,
    SessionPhase Phase,
    IReadOnlyList<BookLevel> Bids,
    IReadOnlyList<BookLevel> Asks,
    DateTimeOffset Timestamp);
