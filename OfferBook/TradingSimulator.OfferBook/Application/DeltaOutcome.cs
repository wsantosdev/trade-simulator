namespace TradingSimulator.OfferBook.Application;

public enum DeltaOutcome
{
    // Contiguous delta applied to the book.
    Applied,
    // Nothing to do: stale delta or snapshot recently requested.
    Ignored,
    // The book has no valid base: a snapshot must be requested from the Exchange.
    SnapshotNeeded
}
