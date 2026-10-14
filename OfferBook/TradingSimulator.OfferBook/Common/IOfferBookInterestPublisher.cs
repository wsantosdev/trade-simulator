using TradingSimulator.OfferBook.Infrastructure.Messaging;

namespace TradingSimulator.OfferBook.Common;

public interface IOfferBookInterestPublisher
{
    Task PublishAsync(EventEnvelope<OfferBookInterestRenewed> envelope, CancellationToken cancellationToken);
}
