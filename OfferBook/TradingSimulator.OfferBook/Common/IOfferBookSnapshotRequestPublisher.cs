using TradingSimulator.OfferBook.Infrastructure.Messaging;

namespace TradingSimulator.OfferBook.Common;

public interface IOfferBookSnapshotRequestPublisher
{
    Task PublishAsync(EventEnvelope<OfferBookSnapshotRequested> envelope, CancellationToken cancellationToken);
}
