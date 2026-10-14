namespace TradingSimulator.Quotes.Infrastructure.Messaging;

// Heartbeat with every ticker that currently has subscribers; the Exchange simulates only these.
public sealed record QuoteInterestRenewed(IReadOnlyList<string> Tickers);
