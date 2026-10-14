namespace TradingSimulator.Exchange.Application;

public interface IOfferBookPublisher
{
    Task PublishSnapshotAsync(EventEnvelope<OfferBookSnapshot> envelope, CancellationToken cancellationToken);
    Task PublishDeltaAsync(EventEnvelope<OfferBookDelta> envelope, CancellationToken cancellationToken);
}
