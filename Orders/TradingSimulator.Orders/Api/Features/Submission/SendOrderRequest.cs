using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Api.Features.Submission;

public sealed record SendOrderRequest(
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    string? RequestHash);
