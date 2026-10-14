using TradingSimulator.Exchange.Infrastructure.Messaging;

namespace TradingSimulator.Exchange.Common;

public interface IOfferBookPublisher
{
    Task PublishSnapshotAsync(EventEnvelope<OfferBookSnapshot> envelope, CancellationToken cancellationToken);
    Task PublishDeltaAsync(EventEnvelope<OfferBookDelta> envelope, CancellationToken cancellationToken);
}
