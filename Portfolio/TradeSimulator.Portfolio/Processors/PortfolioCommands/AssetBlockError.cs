namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public sealed record AssetBlockError(Guid OrderId, Guid AccountId, string Ticker, AssetBlockErrorKind Kind, string Message);
