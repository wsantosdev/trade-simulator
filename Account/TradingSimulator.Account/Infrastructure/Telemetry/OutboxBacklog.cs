namespace TradingSimulator.Account.Infrastructure.Telemetry;

public sealed record OutboxBacklog(string Outbox, long Pending, double OldestAgeSeconds);
