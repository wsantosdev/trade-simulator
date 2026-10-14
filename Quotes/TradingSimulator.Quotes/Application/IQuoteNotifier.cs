namespace TradingSimulator.Quotes.Application;

public interface IQuoteNotifier
{
    Task QuoteUpdatedAsync(Quote quote, CancellationToken cancellationToken);
}
