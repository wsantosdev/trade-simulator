namespace TradingSimulator.Portfolio.Domain.Features.Blocks;

public sealed record AssetBlocked(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
