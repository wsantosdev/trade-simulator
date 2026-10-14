using TradingSimulator.OfferBook.Domain.ValueObjects;

namespace TradingSimulator.OfferBook.Common;

// Asks the Exchange to publish a fresh snapshot right away.
public sealed record OfferBookSnapshotRequested(string Ticker);
