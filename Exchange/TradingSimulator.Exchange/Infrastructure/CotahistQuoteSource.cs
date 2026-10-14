using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using TradingSimulator.Exchange.Application;

namespace TradingSimulator.Exchange.Infrastructure;

// Streams the B3 COTAHIST historical series: fixed-width ASCII/Latin-1 lines of 245 characters.
// Line "00" is the header, "99" the trailer and "01" a daily quote. Only the cash market
// (TPMERC = 010) is kept, which is the market of the instrument register.
// Layout (1-based, B3 "Layout do arquivo de séries históricas"):
//   03-10 DATA (yyyyMMdd)   13-24 CODNEG (ticker)   25-27 TPMERC
//   57-69 PREABE  70-82 PREMAX  83-95 PREMIN  109-121 PREULT   (13 digits, 2 implied decimals)
public sealed class CotahistQuoteSource : IQuoteSource
{
    private const int LineLength = 245;
    private const string CashMarket = "010";

    public async IAsyncEnumerable<ListedQuote> ReadAsync(
        string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path, Encoding.Latin1, false, 65536);

        var first = await reader.ReadLineAsync(cancellationToken);
        if (first is null || !first.StartsWith("00COTAHIST", StringComparison.Ordinal))
            throw new InvalidDataException("The quote file does not start with a COTAHIST header.");

        var lineNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (line.StartsWith("99", StringComparison.Ordinal))
                yield break;
            if (!line.StartsWith("01", StringComparison.Ordinal) || line[24..27] != CashMarket)
                continue;

            yield return Parse(line, lineNumber);
        }
    }

    public static ListedQuote Parse(string line, int lineNumber)
    {
        if (line.Length != LineLength
            || !DateOnly.TryParseExact(line[2..10], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !TryPrice(line[56..69], out var open)
            || !TryPrice(line[69..82], out var high)
            || !TryPrice(line[82..95], out var low)
            || !TryPrice(line[108..121], out var close))
            throw new InvalidDataException($"Malformed quote line {lineNumber}.");

        return new ListedQuote(line[12..24].Trim(), date, open, high, low, close);
    }

    private static bool TryPrice(string digits, out decimal price)
    {
        var ok = long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var cents);
        price = cents / 100m;
        return ok;
    }
}
