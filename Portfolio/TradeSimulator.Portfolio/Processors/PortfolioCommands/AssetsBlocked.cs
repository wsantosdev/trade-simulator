namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public sealed record AssetsBlocked(Guid OrderId, Guid AccountId, string Ticker, long Quantity);
