using TradingSimulator.Exchange.Features.Shared;

namespace TradingSimulator.Exchange.Features.SearchInstruments;

public sealed record InstrumentPage(IReadOnlyList<InstrumentItem> Items, int Page, int PageSize, long TotalCount);

// Inbound port.
public interface ISearchInstruments
{
    Task<InstrumentPage> ExecuteAsync(string? text, int? page, CancellationToken cancellationToken);
}

// Outbound port.
public interface IInstrumentSearch
{
    public const int PageSize = 50;

    Task<InstrumentPage> SearchAsync(string? text, int page, CancellationToken cancellationToken);
}

public sealed class SearchInstrumentsHandler(IInstrumentSearch search) : ISearchInstruments
{
    public Task<InstrumentPage> ExecuteAsync(string? text, int? page, CancellationToken cancellationToken) =>
        search.SearchAsync(text, page ?? 1, cancellationToken);
}
