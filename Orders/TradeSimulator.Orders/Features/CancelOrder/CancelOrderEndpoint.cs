using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Infrastructure.Http;

namespace TradeSimulator.Orders.Features.CancelOrder;

public sealed record CancelOrderRequest(Guid AccountId);

public static class CancelOrderEndpoint
{
    public static IEndpointRouteBuilder MapCancelOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders/{id:guid}/cancel", async (
            Guid id,
            CancelOrderRequest request,
            ICancelOrder cancelOrder,
            CancellationToken cancellationToken) =>
        {
            var result = await cancelOrder.ExecuteAsync(request.AccountId, id, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Accepted($"/api/orders/{id:D}", OrderResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}