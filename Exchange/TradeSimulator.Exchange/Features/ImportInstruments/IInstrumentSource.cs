
namespace TradeSimulator.Exchange.Features.ImportInstruments;

public interface IInstrumentSource
{
    IAsyncEnumerable<ListedInstrument> ReadAsync(string path, CancellationToken cancellationToken);
}
