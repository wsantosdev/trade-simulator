using System.Text.Json;
using TradingSimulator.Portfolio.Domain.Events;

namespace TradingSimulator.Portfolio.Infrastructure.Serialization;

public static class AssetEventSerializer
{
    private static readonly Dictionary<string, Type> Types = new[]
    {
        typeof(AssetDeposited), typeof(AssetBlocked), typeof(AssetWithdrawn), typeof(AssetUnblocked)
    }.ToDictionary(type => type.Name);

    public static string Serialize(AssetEvent @event) =>
        JsonSerializer.Serialize(@event, @event.GetType(), PortfolioJson.Options);

    public static AssetEvent? Deserialize(string eventType, string payload) =>
        Types.TryGetValue(eventType, out var type)
            ? JsonSerializer.Deserialize(payload, type, PortfolioJson.Options) as AssetEvent
            : null;
}
