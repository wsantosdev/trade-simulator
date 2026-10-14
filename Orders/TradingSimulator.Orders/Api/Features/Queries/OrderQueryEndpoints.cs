using System.Text.Json;
using TradingSimulator.Orders.Application;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Api.Features.Queries;

public sealed record SearchOrdersRequest(
    Guid AccountId,
    string? Ticker,
    OrderState? State,
    OrderSide? Side,
    OrderType? Type,
    string? SortBy,
    string? SortDirection,
    int? Page,
    DateTimeOffset? UpdatedSince);

public sealed record OrderHistoryEntryResponse(int Sequence, string EventType, DateTimeOffset OccurredAt, JsonElement Data);

public static class OrderQueryEndpoints
{
    public static IEndpointRouteBuilder MapOrderQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/orders", async (
            [AsParameters] SearchOrdersRequest request,
            IOrderQueries queries,
            CancellationToken cancellationToken) =>
        {
            var account = AccountId.TryCreate(request.AccountId);
            if (account.IsError)
                return account.ErrorValue.ToHttpResult();

            var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "sentAt" : request.SortBy;
            if (!OrderSortFields.Columns.ContainsKey(sortBy))
                return new InvalidSortField().ToHttpResult();

            var descending = !string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
            var page = await queries.SearchAsync(new OrderSearch(
                account.ResultValue,
                request.Ticker,
                request.State,
                request.Side,
                request.Type,
                sortBy,
                descending,
                request.Page ?? 1,
                request.UpdatedSince), cancellationToken);
            return Results.Ok(page);
        });

        endpoints.MapGet("/api/orders/{id:guid}/history", async (
            Guid id,
            Guid accountId,
            IOrderQueries queries,
            CancellationToken cancellationToken) =>
        {
            var account = AccountId.TryCreate(accountId);
            if (account.IsError)
                return account.ErrorValue.ToHttpResult();
            var orderId = OrderId.TryCreate(id);
            if (orderId.IsError)
                return orderId.ErrorValue.ToHttpResult();

            var history = await queries.GetHistoryAsync(account.ResultValue, orderId.ResultValue, cancellationToken);
            if (history.IsError)
                return history.ErrorValue.ToHttpResult();

            return Results.Ok(history.ResultValue.Select(entry => new OrderHistoryEntryResponse(
                entry.Sequence, entry.EventType, entry.OccurredAt, JsonDocument.Parse(entry.Data).RootElement.Clone())));
        });

        return endpoints;
    }
}
