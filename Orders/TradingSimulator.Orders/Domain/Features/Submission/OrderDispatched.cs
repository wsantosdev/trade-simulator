using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Domain.Features.Submission;

public sealed record OrderDispatched(
    OrderId OrderId,
    AccountId AccountId,
    Ticker Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    RequestHash RequestHash,
    DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
