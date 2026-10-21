using TradeSimulator.Account.Domain.Enums;
using TradeSimulator.Account.Domain.ValueObjects;

namespace TradeSimulator.Account.Processors.OrderFunds;

public sealed record OrderSubmission(
    OrderId OrderId,
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price);
