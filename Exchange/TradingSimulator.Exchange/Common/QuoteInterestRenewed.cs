namespace TradingSimulator.Exchange.Common;

// Sent periodically by the Quotes module with every ticker that currently has subscribers.
public sealed record QuoteInterestRenewed(IReadOnlyList<string> Tickers);
