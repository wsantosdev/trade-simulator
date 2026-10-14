namespace TradingSimulator.OfferBook.Application;

// Asks the Exchange to publish a fresh snapshot right away.
public sealed record OfferBookSnapshotRequested(string Ticker);
