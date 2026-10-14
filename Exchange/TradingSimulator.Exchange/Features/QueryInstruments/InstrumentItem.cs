using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.QueryInstruments;

public sealed record InstrumentItem(Guid InstrumentId, string Ticker, string Name);

public sealed record InstrumentPage(IReadOnlyList<InstrumentItem> Items, int Page, int PageSize, long TotalCount);
