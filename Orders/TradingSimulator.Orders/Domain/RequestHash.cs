using System.Text.RegularExpressions;
using Moonad;

namespace TradingSimulator.Orders.Domain;

public readonly partial record struct RequestHash
{
    public string Value { get; }

    private RequestHash(string value) => Value = value;

    public static Result<RequestHash, IOrderError> TryCreate(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && Sha256Hex().IsMatch(normalized)
            ? new RequestHash(normalized)
            : new InvalidRequestHash();
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}
