using TradeSimulator.Exchange.Domain.Entities;

namespace TradeSimulator.Exchange.Domain.ValueObjects;

// ExecutedBefore/RemainingBefore describe the order at the moment it was replaced, before any execution caused by the new price.
public sealed record ReplaceOutcome(BookOrder Order, Quantity ExecutedBefore, Quantity RemainingBefore, IReadOnlyList<Trade> Trades);
