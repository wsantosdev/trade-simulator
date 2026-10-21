namespace TradeSimulator.Exchange.Domain.ValueObjects;

// Unexecuted is the market order balance that was discarded; it is always zero for limit orders (the balance rests).
public sealed record SubmitOutcome(IReadOnlyList<Trade> Trades, Quantity Unexecuted, bool Rested);
