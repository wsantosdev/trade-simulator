using TradingSimulator.Portfolio.Domain.ValueObjects;

namespace TradingSimulator.Portfolio.Domain.Events;

public abstract record AssetEvent(AssetId AssetId, DateTimeOffset OccurredAt);
