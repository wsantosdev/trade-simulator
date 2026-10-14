namespace TradingSimulator.Orders.Domain;

public abstract record OrderEvent(OrderId OrderId, DateTimeOffset OccurredAt);
