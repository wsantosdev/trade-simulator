namespace TradingSimulator.Portfolio.Domain.Features.Unblocks;

public sealed record AssetUnblocked(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
