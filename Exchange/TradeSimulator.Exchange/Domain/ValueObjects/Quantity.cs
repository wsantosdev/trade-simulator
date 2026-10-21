using Moonad;

namespace TradeSimulator.Exchange.Domain.ValueObjects;

public readonly record struct Quantity
{
    public long Value { get; }

    private Quantity(long value) => Value = value;

    public static Quantity Zero => default;

    public bool IsZero => Value == 0;

    public static Result<Quantity, IExchangeError> TryCreate(long value) =>
        value > 0 ? new Quantity(value) : new InvalidQuantity();

    public static Quantity Min(Quantity left, Quantity right) => left.Value <= right.Value ? left : right;

    public Quantity Plus(Quantity other) => new(Value + other.Value);

    // Never goes below zero: the domain only subtracts what it has already checked is available.
    public Quantity Minus(Quantity other) => new(Math.Max(0, Value - other.Value));

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
