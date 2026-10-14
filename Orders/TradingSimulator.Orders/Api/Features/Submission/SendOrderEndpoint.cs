using TradingSimulator.Orders.Application;
using TradingSimulator.Orders.Domain;

namespace TradingSimulator.Orders.Api.Features.Submission;

public static class SendOrderEndpoint
{
    public static IEndpointRouteBuilder MapSendOrderEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/orders", async (
            SendOrderRequest request,
            IOrderStore store,
            IOrderNotifier notifier,
            TimeProvider time,
            CancellationToken cancellationToken) =>
        {
            var order = Order.Send(
                OrderId.New(),
                request.AccountId,
                request.Ticker,
                request.Side,
                request.Type,
                request.Quantity,
                request.Price,
                request.RequestHash,
                time.GetUtcNow());
            if (order.IsError)
                return order.ErrorValue.ToHttpResult();

            var stored = await store.SendAsync(order.ResultValue, cancellationToken);
            if (stored.IsError)
                return stored.ErrorValue.ToHttpResult();

            var sent = stored.ResultValue;
            if (sent.Id == order.ResultValue.Id)
                await notifier.OrderCreatedAsync(OrderView.From(sent), cancellationToken);
            return Results.Created($"/api/orders/{sent.Id.Guid:D}", OrderResponse.From(sent));
        });

        return endpoints;
    }
}
