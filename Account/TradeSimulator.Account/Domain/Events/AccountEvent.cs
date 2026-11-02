using TradeSimulator.Account.Domain.ValueObjects;

using System.Text.Json.Serialization;
using TradeSimulator.EventSourcing;

namespace TradeSimulator.Account.Domain.Events;

public abstract record AccountEvent(AccountId AccountId, DateTimeOffset OccurredAt) : IEvent
{
    [JsonIgnore]
    public Guid StreamId => AccountId.Guid;
}
