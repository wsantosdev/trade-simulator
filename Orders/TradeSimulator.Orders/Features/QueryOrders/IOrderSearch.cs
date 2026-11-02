using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Features.QueryOrders;

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
    // Sort key -> field of the order document in the read model.
    public static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "_id",
        ["ticker"] = "ticker",
        ["side"] = "side",
        ["type"] = "type",
        ["quantity"] = "quantity",
        ["price"] = "price",
        ["averagePrice"] = "averagePrice",
        ["state"] = "state",
        ["executedQuantity"] = "executedQuantity",
        ["remainingQuantity"] = "remainingQuantity",
        ["lastPrice"] = "lastPrice",
        ["lastTradeAt"] = "lastTradeAt",
        ["sentAt"] = "sentAt",
        ["lastActionAt"] = "lastActionAt"
    };
}

public interface IOrderSearch
{
    public const int PageSize = 10;

    Task<OrderPage> SearchAsync(OrderSearch search, CancellationToken cancellationToken);
}
