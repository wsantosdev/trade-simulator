using TradingSimulator.Orders.Application;

namespace TradingSimulator.Orders.Api.Features.Replacement;

public sealed record ReplaceOrderRequest(Guid AccountId, decimal Price);

public static class ReplaceOrderEndpoint
{
    public static IEndpointRouteBuilder MapReplaceOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders/{id:guid}/replace", async (
            Guid id,
            ReplaceOrderRequest request,
            OrderRequestService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RequestReplaceAsync(request.AccountId, id, request.Price, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Accepted($"/api/orders/{id:D}", OrderResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}
