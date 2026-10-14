using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Domain.Entities;

public sealed record AssetSnapshot(AssetId AssetId, AccountId AccountId, Ticker Ticker, long Available, long Blocked, int Version);
