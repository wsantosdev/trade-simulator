using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public interface IQuoteQueries
{
    Task<IReadOnlyList<QuoteItem>> HistoryAsync(Ticker ticker, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
