using TradeSimulator.Exchange.Processors.OrderCommands;

namespace TradeSimulator.Exchange.Processors.OfferBook;

public sealed class OfferBookInterestProcessor(OfferBookInterestRegistry registry, OrderBookEngine engine)
{
    public async Task ProcessAsync(OfferBookInterestCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case RenewOfferBookInterest renew:
                registry.Renew(renew.Tickers);
                break;
            case RequestOfferBookSnapshot request:
                await engine.PublishSnapshotAsync(request.Ticker, cancellationToken);
                break;
        }
    }
}
