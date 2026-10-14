
namespace TradingSimulator.OfferBook.Processors.OfferBookInterest;

public interface IOfferBookInterestPublisher
{
    Task PublishAsync(EventEnvelope<OfferBookInterestRenewed> envelope, CancellationToken cancellationToken);
}
