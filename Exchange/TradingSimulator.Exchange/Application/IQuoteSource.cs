namespace TradingSimulator.Exchange.Application;

public interface IQuoteSource
{
    IAsyncEnumerable<ListedQuote> ReadAsync(string path, CancellationToken cancellationToken);
}
