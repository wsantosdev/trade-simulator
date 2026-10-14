namespace TradingSimulator.Portfolio.Domain;

public abstract record AssetEvent(AssetId AssetId, DateTimeOffset OccurredAt);
