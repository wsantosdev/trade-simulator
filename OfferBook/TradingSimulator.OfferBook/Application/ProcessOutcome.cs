namespace TradingSimulator.OfferBook.Application;

public enum ProcessOutcome
{
    SnapshotStored,
    DeltaForwarded,
    DeltaIgnored,
    SnapshotRequested
}
