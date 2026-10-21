using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Entities;

public sealed record AssetSnapshot(AssetId AssetId, AccountId AccountId, Ticker Ticker, long Available, long Blocked, int Version);
