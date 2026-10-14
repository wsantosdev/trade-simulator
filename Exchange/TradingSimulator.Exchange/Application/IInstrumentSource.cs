namespace TradingSimulator.Exchange.Application;

public interface IInstrumentSource
{
    IAsyncEnumerable<ListedInstrument> ReadAsync(string path, CancellationToken cancellationToken);
}
