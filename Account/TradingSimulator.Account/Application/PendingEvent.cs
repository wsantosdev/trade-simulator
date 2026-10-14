namespace TradingSimulator.Account.Application;

public sealed record PendingEvent(long Position, string EventType, string Payload);
