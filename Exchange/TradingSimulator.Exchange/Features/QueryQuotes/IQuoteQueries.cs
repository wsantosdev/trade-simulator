using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.QueryQuotes;

public interface IQuoteQueries
{
    Task<IReadOnlyList<QuoteItem>> HistoryAsync(Ticker ticker, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
