using TradingSimulator.Quotes.Common;
namespace TradingSimulator.Quotes.Processors.QuoteInterest;

public sealed class QuoteInterestHeartbeat(QuoteSubscriptions subscriptions, IQuoteInterestPublisher publisher)
{
    public Task PublishAsync(CancellationToken cancellationToken)
    {
        var tickers = subscriptions.ActiveTickers().Select(ticker => ticker.Value).ToArray();
        return tickers.Length == 0
            ? Task.CompletedTask
            : publisher.PublishAsync(EventEnvelope.Of(new QuoteInterestRenewed(tickers)), cancellationToken);
    }
}
