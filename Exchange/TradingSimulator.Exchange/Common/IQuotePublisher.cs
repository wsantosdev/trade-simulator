namespace TradingSimulator.Exchange.Common;

public interface IQuotePublisher
{
    Task PublishAsync(EventEnvelope<Quote> envelope, CancellationToken cancellationToken);
}
