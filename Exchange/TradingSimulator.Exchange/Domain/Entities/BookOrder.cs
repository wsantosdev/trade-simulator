using TradingSimulator.Exchange.Domain.Enums;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Entities;

public sealed class BookOrder
{
    private BookOrder(OrderId id, OrderSide side, OrderType type, Price? price, Quantity quantity, OrderOrigin origin)
    {
        Id = id;
        Side = side;
        Type = type;
        Price = price;
        Remaining = quantity;
        Origin = origin;
    }

    public OrderId Id { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }

    // Null for market orders, which never rest on the book.
    public Price? Price { get; internal set; }
    public Quantity Executed { get; private set; }
    public Quantity Remaining { get; private set; }
    public OrderOrigin Origin { get; }

    public static BookOrder Limit(OrderId id, OrderSide side, Price price, Quantity quantity, OrderOrigin origin) =>
        new(id, side, OrderType.Limit, price, quantity, origin);

    public static BookOrder Market(OrderId id, OrderSide side, Quantity quantity, OrderOrigin origin) =>
        new(id, side, OrderType.Market, null, quantity, origin);

    internal void Execute(Quantity quantity)
    {
        Executed = Executed.Plus(quantity);
        Remaining = Remaining.Minus(quantity);
    }

    // Used when the order is replaced: only the price changes, the quantity already executed is kept.
    internal void Reprice(Price price) => Price = price;
}
