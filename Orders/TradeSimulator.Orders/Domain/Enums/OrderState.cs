namespace TradeSimulator.Orders.Domain.Enums;

public enum OrderState : byte
{
    PendingNew = 1,
    New = 2,
    PartiallyFilled = 3,
    Filled = 4,
    Cancelled = 5,
    Rejected = 6,
    PendingCancel = 7,
    PendingReplace = 8,
    PendingDispatching = 9,
    // Funds/assets are reserved but the market is closed: the order stays in Orders until it opens, and is then dispatched.
    ScheduledDispatching = 10
}
