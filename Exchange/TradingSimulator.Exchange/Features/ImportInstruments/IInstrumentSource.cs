using TradingSimulator.Exchange.Common;

namespace TradingSimulator.Exchange.Features.ImportInstruments;

public interface IInstrumentSource
{
    IAsyncEnumerable<ListedInstrument> ReadAsync(string path, CancellationToken cancellationToken);
}
