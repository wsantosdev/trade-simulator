using Moonad;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Features.QueryOrders;

public sealed record SearchOrdersQuery(
    Guid AccountId,
    string? Ticker,
    OrderState? State,
    OrderSide? Side,
    OrderType? Type,
    string? SortBy,
    string? SortDirection,
    int? Page,
    DateTimeOffset? UpdatedSince);

// Inbound port.
public interface IQueryOrders
{
    Task<Result<OrderPage, IOrderError>> SearchAsync(SearchOrdersQuery query, CancellationToken cancellationToken);
}

public sealed class QueryOrdersHandler(IOrderSearch search) : IQueryOrders
{
    public async Task<Result<OrderPage, IOrderError>> SearchAsync(SearchOrdersQuery query, CancellationToken cancellationToken)
    {
        var account = AccountId.TryCreate(query.AccountId);
        if (account.IsError)
            return Result<OrderPage, IOrderError>.Error(account.ErrorValue);

        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "sentAt" : query.SortBy;
        if (!OrderSortFields.Columns.ContainsKey(sortBy))
            return new InvalidSortField();

        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        return await search.SearchAsync(new OrderSearch(
            account.ResultValue,
            query.Ticker,
            query.State,
            query.Side,
            query.Type,
            sortBy,
            descending,
            query.Page ?? 1,
            query.UpdatedSince), cancellationToken);
    }
}
