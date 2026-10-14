using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Features.SubmitOrder;

public sealed record SendOrderRequest(
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    string? RequestHash);
