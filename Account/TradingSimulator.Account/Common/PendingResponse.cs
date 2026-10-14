namespace TradingSimulator.Account.Common;

public sealed record PendingResponse(long Position, string RoutingKey, string Payload);
