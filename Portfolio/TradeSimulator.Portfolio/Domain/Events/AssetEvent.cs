using TradeSimulator.Portfolio.Domain.ValueObjects;

using System.Text.Json.Serialization;
using TradeSimulator.EventSourcing;

namespace TradeSimulator.Portfolio.Domain.Events;

public abstract record AssetEvent(AssetId AssetId, DateTimeOffset OccurredAt) : IEvent
{
    [JsonIgnore]
    public Guid StreamId => AssetId.Guid;
}
