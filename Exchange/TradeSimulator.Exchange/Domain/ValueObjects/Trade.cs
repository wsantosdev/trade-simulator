using TradeSimulator.Exchange.Domain.Entities;

namespace TradeSimulator.Exchange.Domain.ValueObjects;

// One execution at the resting order's price. The remaining quantities are the ones right after this execution.
public sealed record Trade(
    BookOrder Resting,
    BookOrder Aggressor,
    Price Price,
    Quantity Quantity,
    Quantity RestingRemaining,
    Quantity AggressorRemaining);
