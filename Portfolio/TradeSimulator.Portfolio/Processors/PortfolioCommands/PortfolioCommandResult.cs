using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public sealed record PortfolioCommandResult(
    Guid EnvelopeId,
    Guid AssetId,
    Guid AccountId,
    string Ticker,
    long RequestedQuantity,
    long Available,
    long Blocked,
    long Total);
