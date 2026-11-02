using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Infrastructure.Serialization;

namespace TradeSimulator.Portfolio.Tests;

public sealed class PortfolioJsonTests
{
    [Fact]
    public void Round_trips_asset_events_by_type_name()
    {
        var asset = Asset.Open(
            AssetId.New(), AccountId.TryCreate(Guid.CreateVersion7()).ResultValue,
            Ticker.TryCreate("PETR4").ResultValue, Quantity.TryCreate(7).ResultValue, DateTimeOffset.UtcNow).ResultValue;
        var @event = asset.UncommittedEvents[0];

        var store = AssetEventStore.Create();
        var json = store.Serialize(@event);
        var restored = store.Deserialize(@event.GetType().Name, json);

        Assert.Equal(@event, restored);
    }
}
