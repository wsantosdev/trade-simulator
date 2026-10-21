using TradeSimulator.OfferBook.Domain.ValueObjects;

namespace TradeSimulator.OfferBook.Common;

// Asks the Exchange to publish a fresh snapshot right away.
public sealed record OfferBookSnapshotRequested(string Ticker);
