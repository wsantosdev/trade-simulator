using TradeSimulator.Orders.Domain.ValueObjects;

namespace TradeSimulator.Orders.Domain.Events;

// A scheduled order cancelled before dispatch; Account/Portfolio consume it to release the reservation.
public sealed record OrderScheduleCancelled(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
