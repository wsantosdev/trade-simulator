using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

// RawMessage carries the received body when it could not be parsed (the async equivalent of a 400).
public sealed record PortfolioCommandError(
    Guid EnvelopeId,
    Guid? AssetId,
    Guid? AccountId,
    string? Ticker,
    long? RequestedQuantity,
    long? Available,
    long? Blocked,
    long? Total,
    string ErrorType,
    string? RawMessage);
