using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Events;

public sealed record AssetWithdrawn(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
