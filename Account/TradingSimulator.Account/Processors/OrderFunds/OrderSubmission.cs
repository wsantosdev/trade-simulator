using TradingSimulator.Account.Domain.Enums;
using TradingSimulator.Account.Domain.ValueObjects;

namespace TradingSimulator.Account.Processors.OrderFunds;

public sealed record OrderSubmission(
    OrderId OrderId,
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price);
