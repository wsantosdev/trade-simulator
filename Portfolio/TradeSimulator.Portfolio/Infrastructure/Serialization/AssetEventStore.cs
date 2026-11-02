using TradeSimulator.EventSourcing;
using TradeSimulator.Portfolio.Domain.Events;

namespace TradeSimulator.Portfolio.Infrastructure.Serialization;

public static class AssetEventStore
{
    public static EventStore Create() => new(new EventStoreOptions(
        PortfolioJson.Options, [typeof(AssetEvent).Assembly]));
}
