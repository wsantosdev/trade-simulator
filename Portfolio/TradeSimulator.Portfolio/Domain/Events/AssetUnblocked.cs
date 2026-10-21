using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Events;

public sealed record AssetUnblocked(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
