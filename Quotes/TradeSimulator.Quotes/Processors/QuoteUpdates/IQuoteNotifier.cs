using TradeSimulator.Quotes.Common;
namespace TradeSimulator.Quotes.Processors.QuoteUpdates;

public interface IQuoteNotifier
{
    Task QuoteUpdatedAsync(Quote quote, CancellationToken cancellationToken);
}
