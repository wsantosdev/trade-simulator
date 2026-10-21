using Moonad;

namespace TradeSimulator.Portfolio.Domain.ValueObjects;

public readonly record struct Quantity : IComparable<Quantity>
{
    public static readonly Quantity Zero = default;

    public long Value { get; }

    private Quantity(long value) => Value = value;

    public bool IsZero => Value == 0;

    public static Result<Quantity, IPortfolioError> TryCreate(long value) =>
        value > 0 ? new Quantity(value) : new InvalidQuantity();

    public int CompareTo(Quantity other) => Value.CompareTo(other.Value);

    public static bool operator <(Quantity left, Quantity right) => left.Value < right.Value;
    public static bool operator >(Quantity left, Quantity right) => left.Value > right.Value;
    public static bool operator <=(Quantity left, Quantity right) => left.Value <= right.Value;
    public static bool operator >=(Quantity left, Quantity right) => left.Value >= right.Value;

    internal static Quantity Restore(long value) => new(value);

    internal bool CanAdd(Quantity other) => Value <= long.MaxValue - other.Value;

    internal Quantity Add(Quantity other) => new(Value + other.Value);

    internal Quantity Subtract(Quantity other) => new(Value - other.Value);

    public override string ToString() => Value.ToString();
}
