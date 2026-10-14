namespace TradingSimulator.Portfolio.Domain.Features.Withdrawals;

public sealed record AssetWithdrawn(AssetId AssetId, Quantity Quantity, DateTimeOffset OccurredAt)
    : AssetEvent(AssetId, OccurredAt);
