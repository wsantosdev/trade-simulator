using Microsoft.Extensions.Logging.Abstractions;
using TradingSimulator.Exchange.Application;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Tests.Instruments;

public sealed class ImportInstrumentsHandlerTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task Missing_file_returns_typed_error()
    {
        var handler = new ImportInstrumentsHandler(new FakeSource([]), new FakeStore(), NullLogger<ImportInstrumentsHandler>.Instance);

        var result = await handler.HandleAsync(_path + ".missing", CancellationToken.None);

        Assert.IsType<InstrumentFileNotFound>(result.ErrorValue);
    }

    [Fact]
    public async Task Skips_invalid_tickers_and_deduplicates_by_ticker()
    {
        var store = new FakeStore();
        var source = new FakeSource([new("PETR4", "OLD"), new("petr4", "PETROBRAS PN N2"), new("bad!", "X"), new("VALE3", "VALE ON NM")]);
        var handler = new ImportInstrumentsHandler(source, store, NullLogger<ImportInstrumentsHandler>.Instance);

        var result = await handler.HandleAsync(_path, CancellationToken.None);

        Assert.Equal(4, result.ResultValue.Read);
        Assert.Equal(1, result.ResultValue.Skipped);
        Assert.Equal(2, result.ResultValue.Inserted);
        Assert.Equal(["PETR4", "VALE3"], store.Saved.Select(i => i.Ticker.Value));
        Assert.Equal("PETROBRAS PN N2", store.Saved[0].Name);
        Assert.All(store.Saved, i => Assert.Equal(7, i.Id.Guid.Version));
    }

    [Fact]
    public async Task Empty_source_returns_typed_error()
    {
        var handler = new ImportInstrumentsHandler(new FakeSource([]), new FakeStore(), NullLogger<ImportInstrumentsHandler>.Instance);

        var result = await handler.HandleAsync(_path, CancellationToken.None);

        Assert.IsType<NoInstrumentsInFile>(result.ErrorValue);
    }

    [Theory]
    [InlineData("PETR4", true)]
    [InlineData("klbn11", true)]
    [InlineData("M2PM34", true)]
    [InlineData("PETR", false)]
    [InlineData("PETR4F1X", false)]
    [InlineData("", false)]
    public void Ticker_validation(string value, bool valid) =>
        Assert.Equal(valid, !Ticker.TryCreate(value).IsError);

    [Fact]
    public void InstrumentId_rejects_empty_and_non_v7_guids()
    {
        Assert.True(InstrumentId.TryCreate(Guid.Empty).IsError);
        Assert.True(InstrumentId.TryCreate(Guid.NewGuid()).IsError);
        Assert.False(InstrumentId.TryCreate(Guid.CreateVersion7()).IsError);
    }

    private sealed class FakeSource(IReadOnlyList<ListedInstrument> items) : IInstrumentSource
    {
        public async IAsyncEnumerable<ListedInstrument> ReadAsync(
            string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in items)
                yield return item;
            await Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IInstrumentStore
    {
        public List<InstrumentRecord> Saved { get; } = [];

        public Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<InstrumentRecord> instruments, CancellationToken cancellationToken)
        {
            Saved.AddRange(instruments);
            return Task.FromResult(new UpsertOutcome(instruments.Count, 0));
        }
    }
}
