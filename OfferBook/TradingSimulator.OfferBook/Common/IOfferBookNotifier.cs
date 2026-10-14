namespace TradingSimulator.OfferBook.Common;

public interface IOfferBookNotifier
{
    Task SnapshotAsync(OfferBookSnapshot snapshot, CancellationToken cancellationToken);

    Task DeltaAsync(OfferBookDelta delta, CancellationToken cancellationToken);
}
