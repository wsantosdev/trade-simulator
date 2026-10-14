using TradingSimulator.Quotes.Common;
namespace TradingSimulator.Quotes.Processors.QuoteUpdates;

public interface IQuoteNotifier
{
    Task QuoteUpdatedAsync(Quote quote, CancellationToken cancellationToken);
}
