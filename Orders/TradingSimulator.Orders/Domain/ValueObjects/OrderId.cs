using Moonad;

namespace TradingSimulator.Orders.Domain.ValueObjects;

public readonly record struct OrderId
{
    public Guid Guid { get; }

    private OrderId(Guid guid) => Guid = guid;

    public static OrderId New() => new(Guid.CreateVersion7());

    public static Result<OrderId, IOrderError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new OrderId(value)
            : new InvalidOrderId();

    public override string ToString() => Guid.ToString();
}
