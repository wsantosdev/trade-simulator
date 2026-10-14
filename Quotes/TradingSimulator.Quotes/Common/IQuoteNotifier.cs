namespace TradingSimulator.Quotes.Common;

public interface IQuoteNotifier
{
    Task QuoteUpdatedAsync(Quote quote, CancellationToken cancellationToken);
}
