using TradeSimulator.Exchange.Domain.Entities;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public enum OfferBookSide : byte
{
    Bid,
    Ask
}

public sealed record OfferBookLevel(decimal Price, long Quantity, int Orders);

public sealed record OfferBookChange(OfferBookSide Side, decimal Price, long Quantity, int Orders);

public sealed record OfferBookSnapshot(
    string Ticker,
    long Sequence,
    SessionPhase Phase,
    IReadOnlyList<OfferBookLevel> Bids,
    IReadOnlyList<OfferBookLevel> Asks,
    DateTimeOffset Timestamp);

public sealed record OfferBookDelta(
    string Ticker,
    long Sequence,
    long PreviousSequence,
    SessionPhase Phase,
    IReadOnlyList<OfferBookChange> Changes,
    DateTimeOffset Timestamp);

// Sent periodically by the OfferBook module with every ticker that currently has subscribers.
public sealed record OfferBookInterestRenewed(IReadOnlyList<string> Tickers);

public sealed record OfferBookSnapshotRequested(string Ticker);
