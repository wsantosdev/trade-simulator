namespace TradingSimulator.Exchange.Domain.ValueObjects;

public sealed record BookLevel(Price Price, Quantity Quantity, int Orders);
