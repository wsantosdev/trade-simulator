using TradingSimulator.Orders.Domain.ValueObjects;

namespace TradingSimulator.Orders.Domain.Events;

// A scheduled order cancelled before dispatch; Account/Portfolio consume it to release the reservation.
public sealed record OrderScheduleCancelled(OrderId OrderId, DateTimeOffset OccurredAt) : OrderEvent(OrderId, OccurredAt);
