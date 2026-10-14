namespace TradingSimulator.Account.Domain.ValueObjects;

using Moonad;

public readonly record struct OrderId
{
    public Guid Guid { get; }

    private OrderId(Guid guid) => Guid = guid;

    public static OrderId New() => new(Guid.CreateVersion7());

    public static Result<OrderId, IAccountError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new OrderId(value)
            : new InvalidOrderId();

    public override string ToString() => Guid.ToString();
}
