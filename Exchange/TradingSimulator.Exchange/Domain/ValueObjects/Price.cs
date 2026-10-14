using Moonad;

namespace TradingSimulator.Exchange.Domain.ValueObjects;

public readonly record struct Price
{
    public decimal Value { get; }

    private Price(decimal value) => Value = value;

    public static Result<Price, IExchangeError> TryCreate(decimal value) =>
        value > 0 ? new Price(value) : new InvalidPrice();

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
