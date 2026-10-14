namespace TradingSimulator.Account.Application;

public sealed record PendingResponse(long Position, string RoutingKey, string Payload);
