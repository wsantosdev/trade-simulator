using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Processors.Quotes;

public interface IQuotePublisher
{
    Task PublishAsync(Quote quote, CancellationToken cancellationToken);
}
