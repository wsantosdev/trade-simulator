namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

public sealed record AssetCommand(AssetOperation Operation, Guid AccountId, string? Ticker, long Quantity);
