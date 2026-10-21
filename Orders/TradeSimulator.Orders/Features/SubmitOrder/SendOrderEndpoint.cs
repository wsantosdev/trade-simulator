using TradeSimulator.Orders.Infrastructure.Http;

namespace TradeSimulator.Orders.Features.SubmitOrder;

public static class SendOrderEndpoint
{
    public static IEndpointRouteBuilder MapSendOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders", async (
            SendOrderRequest request,
            ISubmitOrder submitOrder,
            CancellationToken cancellationToken) =>
        {
            var result = await submitOrder.ExecuteAsync(new SubmitOrderCommand(
                request.AccountId, request.Ticker, request.Side, request.Type,
                request.Quantity, request.Price, request.RequestHash), cancellationToken);
            return result.IsError
                ? result.ErrorValue.ToHttpResult()
                : Results.Created($"/api/orders/{result.ResultValue.Id.Guid:D}", OrderResponse.From(result.ResultValue));
        });

        return endpoints;
    }
}
