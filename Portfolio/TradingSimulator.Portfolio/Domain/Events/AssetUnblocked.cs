using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Domain.Events;

public sealed record AssetUnblocked(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
