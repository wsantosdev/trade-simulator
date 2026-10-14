namespace TradingSimulator.Orders.Domain;

public enum OrderState
{
    PendingNew = 1,
    New = 2,
    PartiallyFilled = 3,
    Filled = 4,
    Cancelled = 5,
    Rejected = 6,
    PendingCancel = 7,
    PendingReplace = 8,
    PendingDispatching = 9
}
