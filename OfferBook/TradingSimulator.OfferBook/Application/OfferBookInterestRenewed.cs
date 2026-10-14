namespace TradingSimulator.OfferBook.Application;

// Heartbeat with every ticker that currently has subscribers; the Exchange publishes only these books.
public sealed record OfferBookInterestRenewed(IReadOnlyList<string> Tickers);
