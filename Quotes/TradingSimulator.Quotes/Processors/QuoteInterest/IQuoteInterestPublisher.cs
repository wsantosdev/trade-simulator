namespace TradingSimulator.Quotes.Processors.QuoteInterest;

public interface IQuoteInterestPublisher
{
    Task PublishAsync(EventEnvelope<QuoteInterestRenewed> envelope, CancellationToken cancellationToken);
}
