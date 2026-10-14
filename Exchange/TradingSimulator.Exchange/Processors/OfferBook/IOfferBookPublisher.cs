namespace TradingSimulator.Exchange.Processors.OfferBook;

public interface IOfferBookPublisher
{
    Task PublishSnapshotAsync(BookSnapshot snapshot, CancellationToken cancellationToken);
    Task PublishDeltaAsync(BookDelta delta, CancellationToken cancellationToken);
}
