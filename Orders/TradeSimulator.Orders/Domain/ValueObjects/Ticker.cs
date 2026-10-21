using System.Text.RegularExpressions;
using Moonad;

namespace TradeSimulator.Orders.Domain.ValueObjects;

public readonly partial record struct Ticker
{
    public string Value { get; }

    private Ticker(string value) => Value = value;

    public static Result<Ticker, IOrderError> TryCreate(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && StockTicker().IsMatch(normalized)
            ? new Ticker(normalized)
            : new InvalidTicker();
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z]{4}[345]$")]
    private static partial Regex StockTicker();
}
