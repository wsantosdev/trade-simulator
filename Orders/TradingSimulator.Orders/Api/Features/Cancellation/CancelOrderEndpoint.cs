using TradingSimulator.Orders.Application;

namespace TradingSimulator.Orders.Api.Features.Cancellation;

public sealed record CancelOrderRequest(Guid AccountId);

public static class CancelOrderEndpoint
{
    public static IEndpointRouteBuilder MapCancelOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders/{id:guid}/cancel", async (
            Guid id,
            CancelOrderRequest request,
            OrderRequestService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RequestCancelAsync(request.AccountId, id, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Accepted($"/api/orders/{id:D}", OrderResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}
