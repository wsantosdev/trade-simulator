using TradeSimulator.Outbox;

namespace TradeSimulator.Account.Infrastructure.Messaging;

public sealed record PendingResponse(long Position, string RoutingKey, string Payload, string? TraceParent = null) : IOutboxItem;
