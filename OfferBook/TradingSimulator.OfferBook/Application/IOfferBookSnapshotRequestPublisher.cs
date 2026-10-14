namespace TradingSimulator.OfferBook.Application;

public interface IOfferBookSnapshotRequestPublisher
{
    Task PublishAsync(EventEnvelope<OfferBookSnapshotRequested> envelope, CancellationToken cancellationToken);
}
