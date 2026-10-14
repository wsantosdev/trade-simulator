using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Domain.Events;

public sealed record AssetWithdrawn(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
