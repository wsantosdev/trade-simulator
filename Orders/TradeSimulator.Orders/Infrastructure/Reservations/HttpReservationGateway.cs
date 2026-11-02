using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Processors;

namespace TradeSimulator.Orders.Infrastructure.Reservations;

// Calls Account (buy) or Portfolio (sell) synchronously. A 422 is a business rejection; anything else is unavailability.
public sealed class HttpReservationGateway(
    IHttpClientFactory factory,
    ILogger<HttpReservationGateway> logger) : IReservationGateway
{
    public const string Account = "Account";
    public const string Portfolio = "Portfolio";

    public async Task<ReservationOutcome> ReserveAsync(Order order, CancellationToken cancellationToken)
    {
        var service = order.Side == OrderSide.Buy ? Account : Portfolio;
        object body = order.Side == OrderSide.Buy
            ? new
            {
                orderId = order.Id.Guid,
                accountId = order.AccountId.Guid,
                ticker = order.Ticker.Value,
                side = order.Side.ToString(),
                type = order.Type.ToString(),
                quantity = order.Quantity,
                price = order.Price
            }
            : new
            {
                orderId = order.Id.Guid,
                accountId = order.AccountId.Guid,
                ticker = order.Ticker.Value,
                quantity = order.Quantity
            };

        try
        {
            using var response = await factory.CreateClient(service)
                .PostAsJsonAsync("api/order-reservations", body, cancellationToken);
            if (response.IsSuccessStatusCode)
                return ReservationOutcome.Ok();

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                var reason = content.TryGetProperty("reason", out var value) ? value.GetString() : null;
                return ReservationOutcome.Rejected(reason);
            }

            logger.LogWarning("{Service} answered {Status} for order {OrderId}.", service, (int)response.StatusCode, order.Id);
            return ReservationOutcome.ServiceUnavailable(service);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "{Service} is unavailable for order {OrderId}.", service, order.Id);
            return ReservationOutcome.ServiceUnavailable(service);
        }
    }

    public async Task ReleaseAsync(Order order, CancellationToken cancellationToken)
    {
        var service = order.Side == OrderSide.Buy ? Account : Portfolio;
        try
        {
            using var response = await factory.CreateClient(service)
                .DeleteAsync($"api/order-reservations/{order.Id.Guid:D}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("{Service} could not release the reservation of order {OrderId}: {Status}.", service, order.Id, (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not release the reservation of order {OrderId} on {Service}.", order.Id, service);
        }
    }
}
