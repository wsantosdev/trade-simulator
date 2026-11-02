namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

// Notification that an event of the asset's stream was appended; the read model reacts to it.
public sealed record AssetChanged(Guid AssetId, int Version, string AssetEventType)
{
    public const string EventType = "AssetChanged";
    public const string RoutingKey = "asset.changed";
}
