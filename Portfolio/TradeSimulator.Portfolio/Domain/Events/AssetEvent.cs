using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Domain.Events;

public abstract record AssetEvent(AssetId AssetId, DateTimeOffset OccurredAt);
