using System.Text.Json.Serialization;
using TradeSimulator.Account.Domain.Enums;
using TradeSimulator.Account.Domain.ValueObjects;
using TradeSimulator.Account.Processors.OrderFunds;

namespace TradeSimulator.Account.Features.SubmitReservation;

public static class OrderReservationEndpoints
{
    public static IEndpointRouteBuilder MapOrderReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Synchronous validation of a buy order: 200 when the funds were reserved, 422 with the reason otherwise.
        endpoints.MapPost("/api/order-reservations", async (
            OrderReservationRequest request,
            OrderFundsProcessor processor,
            CancellationToken cancellationToken) =>
        {
            var orderId = OrderId.TryCreate(request.OrderId);
            if (orderId.IsError)
                return Results.Json(new OrderReservationResponse(false, nameof(FundsReservationErrorKind.InvalidOrder), 0m), statusCode: StatusCodes.Status422UnprocessableEntity);

            var result = await processor.ReserveAsync(
                new OrderSubmission(orderId.ResultValue, request.AccountId, request.Ticker, request.Side, request.Type, request.Quantity, request.Price),
                cancellationToken);
            var body = new OrderReservationResponse(result.Reserved, result.Kind?.ToString(), result.Amount);
            return result.Reserved
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        endpoints.MapDelete("/api/order-reservations/{orderId:guid}", async (
            Guid orderId,
            OrderFundsProcessor processor,
            CancellationToken cancellationToken) =>
        {
            var id = OrderId.TryCreate(orderId);
            if (id.IsError)
                return Results.BadRequest();
            await processor.ReleaseAsync(id.ResultValue, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }
}

public sealed record OrderReservationRequest(
    Guid OrderId,
    Guid AccountId,
    string? Ticker,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OrderSide Side,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OrderType Type,
    long Quantity,
    decimal? Price);

public sealed record OrderReservationResponse(bool Reserved, string? Reason, decimal Amount);
