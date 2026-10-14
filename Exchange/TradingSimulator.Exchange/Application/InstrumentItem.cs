namespace TradingSimulator.Exchange.Application;

public sealed record InstrumentItem(Guid InstrumentId, string Ticker, string Name);

public sealed record InstrumentPage(IReadOnlyList<InstrumentItem> Items, int Page, int PageSize, long TotalCount);
