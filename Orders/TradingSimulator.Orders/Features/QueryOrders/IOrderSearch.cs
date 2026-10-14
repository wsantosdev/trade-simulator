using TradingSimulator.Orders.Common;
using TradingSimulator.Orders.Domain.Enums;
using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Features.QueryOrders;

public sealed record OrderSearch(
    AccountId AccountId,
    string? Ticker,
    OrderState? State,
    OrderSide? Side,
    OrderType? Type,
    string SortBy,
    bool Descending,
    int Page,
    DateTimeOffset? UpdatedSince);

public sealed record OrderPage(IReadOnlyList<OrderView> Items, int Page, int PageSize, long TotalCount);

public static class OrderSortFields
{
    public static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "order_id",
        ["ticker"] = "ticker",
        ["side"] = "side",
        ["type"] = "type",
        ["quantity"] = "quantity",
        ["price"] = "price",
        ["averagePrice"] = "average_price",
        ["state"] = "state",
        ["executedQuantity"] = "executed_quantity",
        ["remainingQuantity"] = "remaining_quantity",
        ["lastPrice"] = "last_price",
        ["lastTradeAt"] = "last_trade_at",
        ["sentAt"] = "sent_at",
        ["lastActionAt"] = "last_action_at"
    };
}

public interface IOrderSearch
{
    public const int PageSize = 10;

    Task<OrderPage> SearchAsync(OrderSearch search, CancellationToken cancellationToken);
}
