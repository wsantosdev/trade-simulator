using TradingSimulator.Orders.Infrastructure.Http;

namespace TradingSimulator.Orders.Features.QueryOrders;

public static class SearchOrdersEndpoint
{
    public static IEndpointRouteBuilder MapSearchOrdersEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/orders", async (
            [AsParameters] SearchOrdersQuery query,
            IQueryOrders orders,
            CancellationToken cancellationToken) =>
        {
            var page = await orders.SearchAsync(query, cancellationToken);
            return page.IsError ? page.ErrorValue.ToHttpResult() : Results.Ok(page.ResultValue);
        });

        return endpoints;
    }
}
