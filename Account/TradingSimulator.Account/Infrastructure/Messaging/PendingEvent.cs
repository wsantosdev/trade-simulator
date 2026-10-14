namespace TradingSimulator.Account.Infrastructure.Messaging;

public sealed record PendingEvent(long Position, string EventType, string Payload, string? TraceParent = null);
