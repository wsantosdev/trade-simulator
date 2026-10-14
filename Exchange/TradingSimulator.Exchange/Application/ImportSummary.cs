namespace TradingSimulator.Exchange.Application;

public sealed record ImportSummary(int Read, int Skipped, int Inserted, int Updated, int Unchanged);
