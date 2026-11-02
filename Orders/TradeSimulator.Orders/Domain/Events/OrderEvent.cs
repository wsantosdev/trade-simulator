using TradeSimulator.Orders.Domain.ValueObjects;

using System.Text.Json.Serialization;
using TradeSimulator.EventSourcing;

namespace TradeSimulator.Orders.Domain.Events;

public abstract record OrderEvent(OrderId OrderId, DateTimeOffset OccurredAt) : IEvent
{
    [JsonIgnore]
    public Guid StreamId => OrderId.Guid;
}
