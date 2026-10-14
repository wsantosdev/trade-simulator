using Microsoft.Extensions.Logging.Abstractions;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Features.ImportQuotes;
using TradingSimulator.Exchange.Infrastructure.Sources;

namespace TradingSimulator.Exchange.Tests.Instruments;

public sealed class QuotesTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    private static string Line(string kind, string ticker, string market, long open, long high, long low, long close) =>
        $"{kind}20260107" + "34" + ticker.PadRight(12) + market + "NAME".PadRight(12) + "".PadRight(10) + "".PadRight(3) + "R$  "
        + open.ToString("D13") + high.ToString("D13") + low.ToString("D13") + "0".PadLeft(13, '0') + close.ToString("D13")
        + "".PadRight(245 - 121);

    [Fact]
    public async Task Cotahist_reader_keeps_only_cash_market_quotes_and_converts_cents()
    {
        var header = "00COTAHIST.2026BOVESPA 20261013".PadRight(245);
        var trailer = "99COTAHIST.2026BOVESPA 20261013".PadRight(245);
        await File.WriteAllLinesAsync(_path,
            [header, Line("01", "PETR4", "010", 4585, 4726, 4580, 4634), Line("01", "PETRA100", "070", 1, 1, 1, 1), trailer]);

        var quotes = new List<ListedQuote>();
        await foreach (var quote in new CotahistQuoteSource().ReadAsync(_path, CancellationToken.None))
            quotes.Add(quote);

        Assert.Equal([new ListedQuote("PETR4", new DateOnly(2026, 1, 7), 45.85m, 47.26m, 45.80m, 46.34m)], quotes);
    }

    [Fact]
    public async Task Cotahist_reader_rejects_file_without_header()
    {
        await File.WriteAllTextAsync(_path, "not a cotahist file");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in new CotahistQuoteSource().ReadAsync(_path, CancellationToken.None)) { }
        });
    }

    [Theory]
    [InlineData(10, 12, 9, 11, true)]
    [InlineData(10, 12, 9, 13, false)]
    [InlineData(8, 12, 9, 11, false)]
    [InlineData(0, 12, 9, 11, false)]
    public void Daily_quote_invariants(decimal open, decimal high, decimal low, decimal close, bool valid) =>
        Assert.Equal(valid, !DailyQuote.TryCreate("PETR4", new DateOnly(2026, 1, 2), open, high, low, close).IsError);
}
