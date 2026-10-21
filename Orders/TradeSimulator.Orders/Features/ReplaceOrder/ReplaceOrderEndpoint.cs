using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Infrastructure.Http;

namespace TradeSimulator.Orders.Features.ReplaceOrder;

public sealed record ReplaceOrderRequest(Guid AccountId, decimal Price);

public static class ReplaceOrderEndpoint
{
    public static IEndpointRouteBuilder MapReplaceOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders/{id:guid}/replace", async (
            Guid id,
            ReplaceOrderRequest request,
            IReplaceOrder replaceOrder,
            CancellationToken cancellationToken) =>
        {
            var result = await replaceOrder.ExecuteAsync(request.AccountId, id, request.Price, cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Accepted($"/api/orders/{id:D}", OrderResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}
