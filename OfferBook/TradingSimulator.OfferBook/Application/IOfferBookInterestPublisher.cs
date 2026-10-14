namespace TradingSimulator.OfferBook.Application;

public interface IOfferBookInterestPublisher
{
    Task PublishAsync(EventEnvelope<OfferBookInterestRenewed> envelope, CancellationToken cancellationToken);
}
