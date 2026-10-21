using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Processors.Quotes;

public interface IQuotePublisher
{
    Task PublishAsync(Quote quote, CancellationToken cancellationToken);
}
