using TradeSimulator.OfferBook.Common;

namespace TradeSimulator.OfferBook.Processors.OfferBookInterest;

public sealed class OfferBookInterestHeartbeat(
    OfferBookSubscriptions subscriptions,
    IOfferBookInterestPublisher interestPublisher,
    IOfferBookSnapshotRequestPublisher snapshotRequests)
{
    public Task PublishAsync(CancellationToken cancellationToken)
    {
        var tickers = subscriptions.ActiveTickers().Select(ticker => ticker.Value).ToArray();
        return tickers.Length == 0
            ? Task.CompletedTask
            : interestPublisher.PublishAsync(EventEnvelope.Of(new OfferBookInterestRenewed(tickers)), cancellationToken);
    }

    // First subscriber of a ticker: renew the interest at once and ask for the current book.
    public async Task FirstSubscriberAsync(string ticker, CancellationToken cancellationToken)
    {
        await PublishAsync(cancellationToken);
        await snapshotRequests.PublishAsync(EventEnvelope.Of(new OfferBookSnapshotRequested(ticker)), cancellationToken);
    }
}
