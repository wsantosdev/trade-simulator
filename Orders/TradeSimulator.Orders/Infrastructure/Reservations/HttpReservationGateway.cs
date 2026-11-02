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
    IOfferBookClient offerBook,
    ILogger<HttpReservationGateway> logger) : IReservationGateway
{
    public const string Account = "Account";
    public const string Portfolio = "Portfolio";
    private const decimal MarketMargin = 1.05m;

    public Task<ReservationOutcome> ReserveAsync(Order order, CancellationToken cancellationToken) =>
        order.Side == OrderSide.Buy ? ReserveFundsAsync(order, cancellationToken) : ReservePositionAsync(order, cancellationToken);

    private async Task<ReservationOutcome> ReserveFundsAsync(Order order, CancellationToken cancellationToken)
    {
        decimal reference;
        if (order.Type == OrderType.Limit)
            reference = order.Price ?? 0m;
        else
        {
            var bestAsk = await offerBook.GetBestAskAsync(order.Ticker.Value, cancellationToken);
            if (bestAsk is null)
                return ReservationOutcome.Rejected("PriceUnavailable");
            reference = bestAsk.Value;
        }

        decimal amount;
        try
        {
            var factor = order.Type == OrderType.Market ? MarketMargin : 1m;
            amount = Math.Ceiling(checked(order.Quantity * reference * factor) * 100m) / 100m;
        }
        catch (OverflowException)
        {
            return ReservationOutcome.Rejected("InvalidOrder");
        }

        if (amount <= 0)
            return ReservationOutcome.Rejected("InvalidOrder");

        try
        {
            using var response = await factory.CreateClient(Account).PostAsJsonAsync(
                $"api/accounts/{order.AccountId.Guid:D}/reservations",
                new { reservationId = order.Id.Guid, amount },
                cancellationToken);
            if (response.IsSuccessStatusCode)
                return ReservationOutcome.Ok();

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return ReservationOutcome.Rejected("AccountNotFound");
                case HttpStatusCode.UnprocessableEntity:
                    return ReservationOutcome.Rejected("InsufficientFunds");
                case HttpStatusCode.BadRequest or HttpStatusCode.Conflict:
                    return ReservationOutcome.Rejected("InvalidOrder");
            }

            logger.LogWarning("Account answered {Status} for order {OrderId}.", (int)response.StatusCode, order.Id);
            return ReservationOutcome.ServiceUnavailable(Account);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Account is unavailable for order {OrderId}.", order.Id);
            return ReservationOutcome.ServiceUnavailable(Account);
        }
    }

    private async Task<ReservationOutcome> ReservePositionAsync(Order order, CancellationToken cancellationToken)
    {
        var body = new
        {
            reservationId = order.Id.Guid,
            accountId = order.AccountId.Guid,
            ticker = order.Ticker.Value,
            quantity = order.Quantity
        };

        try
        {
            using var response = await factory.CreateClient(Portfolio)
                .PostAsJsonAsync("api/assets/reservations", body, cancellationToken);
            if (response.IsSuccessStatusCode)
                return ReservationOutcome.Ok();

            switch (response.StatusCode)
            {
                case HttpStatusCode.UnprocessableEntity:
                    var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                    var reason = content.TryGetProperty("reason", out var value) ? value.GetString() : null;
                    return ReservationOutcome.Rejected(reason);
                case HttpStatusCode.NotFound:
                    return ReservationOutcome.Rejected("AssetNotFound");
                case HttpStatusCode.BadRequest or HttpStatusCode.Conflict:
                    return ReservationOutcome.Rejected("InvalidOrder");
            }

            logger.LogWarning("Portfolio answered {Status} for order {OrderId}.", (int)response.StatusCode, order.Id);
            return ReservationOutcome.ServiceUnavailable(Portfolio);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Portfolio is unavailable for order {OrderId}.", order.Id);
            return ReservationOutcome.ServiceUnavailable(Portfolio);
        }
    }
}
