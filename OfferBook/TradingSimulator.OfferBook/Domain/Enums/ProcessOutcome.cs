namespace TradingSimulator.OfferBook.Domain.Enums;

public enum ProcessOutcome : byte
{
    SnapshotStored,
    DeltaForwarded,
    DeltaIgnored,
    SnapshotRequested
}
