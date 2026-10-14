namespace TradingSimulator.Account.Common;

public sealed record PendingEvent(long Position, string EventType, string Payload);
