namespace TradingSimulator.Portfolio.Domain;

public sealed record AssetSnapshot(AssetId AssetId, AccountId AccountId, Ticker Ticker, long Available, long Blocked, int Version);
