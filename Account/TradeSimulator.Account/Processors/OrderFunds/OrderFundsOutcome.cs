namespace TradeSimulator.Account.Processors.OrderFunds;

public enum OrderFundsOutcome : byte
{
    Applied,
    // The envelope (or the order) was already handled: nothing was changed.
    Duplicate,
    // No reservation exists (yet) for the order.
    UnknownOrder,
    // The order is known to have no reservation to settle (sell, failed reservation or already settled).
    Ignored
}
