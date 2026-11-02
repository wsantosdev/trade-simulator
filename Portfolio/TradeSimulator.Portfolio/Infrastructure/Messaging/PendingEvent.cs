using TradeSimulator.Outbox;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed record PendingEvent(long Position, string RoutingKey, string Payload, string? TraceParent = null) : IOutboxItem;
