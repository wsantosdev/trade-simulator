using System.Text.RegularExpressions;
using Moonad;

namespace TradingSimulator.OfferBook.Domain.ValueObjects;

public readonly partial record struct Ticker
{
    public string Value { get; }

    private Ticker(string value) => Value = value;

    public static Result<Ticker, IOfferBookError> TryCreate(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && ListedTicker().IsMatch(normalized)
            ? new Ticker(normalized)
            : new InvalidTicker();
    }

    public override string ToString() => Value;

    // Cash-market tickers: stocks (PETR4), units (KLBN11), FIIs, ETFs and BDRs (ACNB34, M2PM34).
    [GeneratedRegex("^[A-Z][A-Z0-9]{3}[0-9]{1,2}$")]
    private static partial Regex ListedTicker();
}
