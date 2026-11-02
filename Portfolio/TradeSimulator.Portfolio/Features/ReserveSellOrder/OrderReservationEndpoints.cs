using TradeSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradeSimulator.Portfolio.Features.ReserveSellOrder;

public static class OrderReservationEndpoints
{
    public static IEndpointRouteBuilder MapOrderReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Synchronous validation of a sell order: 200 when the quantity was blocked, 422 with the reason otherwise.
        endpoints.MapPost("/api/order-reservations", async (
            OrderReservationRequest request,
            PortfolioCommandProcessor processor,
            CancellationToken cancellationToken) =>
        {
            var result = await processor.ReserveSyncAsync(
                new SellOrder(request.OrderId, request.AccountId, request.Ticker, request.Quantity), cancellationToken);
            var body = new OrderReservationResponse(result.Reserved, result.Kind?.ToString());
            return result.Reserved
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        endpoints.MapDelete("/api/order-reservations/{orderId:guid}", async (
            Guid orderId,
            PortfolioCommandProcessor processor,
            CancellationToken cancellationToken) =>
        {
            await processor.ReleaseSyncAsync(orderId, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }
}

public sealed record OrderReservationRequest(Guid OrderId, Guid AccountId, string? Ticker, long Quantity);

public sealed record OrderReservationResponse(bool Reserved, string? Reason);
