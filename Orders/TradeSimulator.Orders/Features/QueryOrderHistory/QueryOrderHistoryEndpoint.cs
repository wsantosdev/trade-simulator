using System.Text.Json;
using TradeSimulator.Orders.Infrastructure.Http;

namespace TradeSimulator.Orders.Features.QueryOrderHistory;

public sealed record OrderHistoryEntryResponse(int Sequence, string EventType, DateTimeOffset OccurredAt, JsonElement Data);

public static class QueryOrderHistoryEndpoint
{
    public static IEndpointRouteBuilder MapQueryOrderHistoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/orders/{id:guid}/history", async (
            Guid id,
            Guid accountId,
            IQueryOrderHistory queryHistory,
            CancellationToken cancellationToken) =>
        {
            var history = await queryHistory.ExecuteAsync(accountId, id, cancellationToken);
            if (history.IsError)
                return history.ErrorValue.ToHttpResult();

            return Results.Ok(history.ResultValue.Select(entry => new OrderHistoryEntryResponse(
                entry.Sequence, entry.EventType, entry.OccurredAt, JsonDocument.Parse(entry.Data).RootElement.Clone())));
        });

        return endpoints;
    }
}
