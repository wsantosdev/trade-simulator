namespace TradingSimulator.Exchange.Common;

public sealed record ImportSummary(int Read, int Skipped, int Inserted, int Updated, int Unchanged);
