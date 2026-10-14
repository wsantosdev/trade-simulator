using TradingSimulator.Quotes.Infrastructure.Messaging;

namespace TradingSimulator.Quotes.Common;

public interface IQuoteInterestPublisher
{
    Task PublishAsync(EventEnvelope<QuoteInterestRenewed> envelope, CancellationToken cancellationToken);
}
