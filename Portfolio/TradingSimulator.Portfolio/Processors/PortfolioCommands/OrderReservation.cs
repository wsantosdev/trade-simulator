namespace TradingSimulator.Portfolio.Processors.PortfolioCommands;

// Per-order custody of a sell order: Blocked is what was reserved, Remaining what is still blocked for it.
public sealed record OrderReservation(Guid OrderId, Guid AccountId, string Ticker, long Blocked, long Remaining, bool Closed);
