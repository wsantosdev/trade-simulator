namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

public sealed record SellOrder(Guid OrderId, Guid AccountId, string? Ticker, long Quantity);
