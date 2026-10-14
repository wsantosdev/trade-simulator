namespace TradingSimulator.Orders.Common;

public sealed record PendingEvent(long Position, string EventType, string Payload);
