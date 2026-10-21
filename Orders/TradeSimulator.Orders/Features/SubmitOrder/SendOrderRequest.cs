using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Features.SubmitOrder;

public sealed record SendOrderRequest(
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    string? RequestHash);
