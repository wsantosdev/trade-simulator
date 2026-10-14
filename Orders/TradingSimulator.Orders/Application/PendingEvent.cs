namespace TradingSimulator.Orders.Application;

public sealed record PendingEvent(long Position, string EventType, string Payload);
