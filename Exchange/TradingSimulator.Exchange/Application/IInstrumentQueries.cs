using Moonad;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public interface IInstrumentQueries
{
    public const int PageSize = 50;

    Task<InstrumentPage> SearchAsync(string? text, int page, CancellationToken cancellationToken);

    Task<Result<InstrumentItem, IExchangeError>> GetAsync(Ticker ticker, CancellationToken cancellationToken);
}
