using TradeSimulator.Orders.Domain.Entities;

namespace TradeSimulator.Orders.Processors;

// Synchronous admission of an order: buy orders reserve funds (Account), sell orders block the position (Portfolio).
// Account and Portfolio know nothing about orders: Orders computes what to reserve. They settle and release
// the reservation by listening to the order events Orders publishes (the order id is the reservation id).
public interface IReservationGateway
{
    Task<ReservationOutcome> ReserveAsync(Order order, CancellationToken cancellationToken);
}

// Best ask of an instrument, used to size the reservation of market buy orders.
public interface IOfferBookClient
{
    Task<decimal?> GetBestAskAsync(string ticker, CancellationToken cancellationToken);
}

public sealed record ReservationOutcome(bool Reserved, string? Reason, bool Unavailable = false)
{
    public static ReservationOutcome Ok() => new(true, null);
    public static ReservationOutcome Rejected(string? reason) => new(false, reason ?? "ReservationRejected");
    public static ReservationOutcome ServiceUnavailable(string service) => new(false, $"{service}Unavailable", true);
}
