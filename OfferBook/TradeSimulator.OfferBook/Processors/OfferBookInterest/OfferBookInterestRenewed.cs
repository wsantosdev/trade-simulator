namespace TradeSimulator.OfferBook.Processors.OfferBookInterest;

// Heartbeat with every ticker that currently has subscribers; the Exchange publishes only these books.
public sealed record OfferBookInterestRenewed(IReadOnlyList<string> Tickers);
