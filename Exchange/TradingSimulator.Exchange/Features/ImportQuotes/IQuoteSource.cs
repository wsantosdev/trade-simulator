using TradingSimulator.Exchange.Common;

namespace TradingSimulator.Exchange.Features.ImportQuotes;

public interface IQuoteSource
{
    IAsyncEnumerable<ListedQuote> ReadAsync(string path, CancellationToken cancellationToken);
}
