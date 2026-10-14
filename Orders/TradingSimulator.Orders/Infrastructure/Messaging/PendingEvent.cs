namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed record PendingEvent(long Position, string EventType, string Payload);
