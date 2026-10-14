namespace TradingSimulator.Quotes.Application;

public interface IQuoteInterestPublisher
{
    Task PublishAsync(EventEnvelope<QuoteInterestRenewed> envelope, CancellationToken cancellationToken);
}
