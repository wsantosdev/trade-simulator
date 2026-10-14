namespace TradingSimulator.Exchange.Domain;

public sealed record BookLevel(Price Price, Quantity Quantity, int Orders);
