using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Events;

public sealed record AssetDeposited(
    AssetId AssetId,
    AccountId AccountId,
    Ticker Ticker,
    Quantity Quantity,
    DateTimeOffset OccurredAt) : AssetEvent(AssetId, OccurredAt);
