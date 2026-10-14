namespace TradingSimulator.Portfolio.Application;

public sealed record PortfolioCommandResult(
    Guid EnvelopeId,
    Guid AssetId,
    Guid AccountId,
    string Ticker,
    long RequestedQuantity,
    long Available,
    long Blocked,
    long Total);
