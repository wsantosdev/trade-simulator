using TradeSimulator.Orders.Domain.Entities;

namespace TradeSimulator.Orders.Processors;

// Synchronous admission of an order: buy orders reserve funds (Account), sell orders block the position (Portfolio).
public interface IReservationGateway
{
    Task<ReservationOutcome> ReserveAsync(Order order, CancellationToken cancellationToken);

    // Best-effort compensation when the order could not proceed after the reservation.
    Task ReleaseAsync(Order order, CancellationToken cancellationToken);
}

public sealed record ReservationOutcome(bool Reserved, string? Reason, bool Unavailable = false)
{
    public static ReservationOutcome Ok() => new(true, null);
    public static ReservationOutcome Rejected(string? reason) => new(false, reason ?? "ReservationRejected");
    public static ReservationOutcome ServiceUnavailable(string service) => new(false, $"{service}Unavailable", true);
}
