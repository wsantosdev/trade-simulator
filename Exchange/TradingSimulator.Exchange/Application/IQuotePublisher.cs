namespace TradingSimulator.Exchange.Application;

public interface IQuotePublisher
{
    Task PublishAsync(EventEnvelope<Quote> envelope, CancellationToken cancellationToken);
}
