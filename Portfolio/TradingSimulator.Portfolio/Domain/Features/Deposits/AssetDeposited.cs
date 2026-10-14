namespace TradingSimulator.Portfolio.Domain.Features.Deposits;

public sealed record AssetDeposited(
    AssetId AssetId,
    AccountId AccountId,
    Ticker Ticker,
    Quantity Quantity,
    DateTimeOffset OccurredAt) : AssetEvent(AssetId, OccurredAt);
