using TradingSimulator.Exchange.Domain.Entities;

namespace TradingSimulator.Exchange.Domain.ValueObjects;

// One execution at the resting order's price. The remaining quantities are the ones right after this execution.
public sealed record Trade(
    BookOrder Resting,
    BookOrder Aggressor,
    Price Price,
    Quantity Quantity,
    Quantity RestingRemaining,
    Quantity AggressorRemaining);
