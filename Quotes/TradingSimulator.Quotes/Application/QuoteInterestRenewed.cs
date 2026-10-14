namespace TradingSimulator.Quotes.Application;

// Heartbeat with every ticker that currently has subscribers; the Exchange simulates only these.
public sealed record QuoteInterestRenewed(IReadOnlyList<string> Tickers);
