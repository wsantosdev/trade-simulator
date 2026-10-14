namespace TradingSimulator.OfferBook.Common;

public interface IOfferBookSnapshotRequestPublisher
{
    Task PublishAsync(EventEnvelope<OfferBookSnapshotRequested> envelope, CancellationToken cancellationToken);
}
