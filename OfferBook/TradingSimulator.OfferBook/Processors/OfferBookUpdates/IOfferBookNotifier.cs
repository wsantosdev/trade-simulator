using TradingSimulator.OfferBook.Common;
namespace TradingSimulator.OfferBook.Processors.OfferBookUpdates;

public interface IOfferBookNotifier
{
    Task SnapshotAsync(OfferBookSnapshot snapshot, CancellationToken cancellationToken);

    Task DeltaAsync(OfferBookDelta delta, CancellationToken cancellationToken);
}
