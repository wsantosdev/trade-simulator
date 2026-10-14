using Moonad;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public interface IQuoteSimulationSource
{
    Task<Result<SimulationParameters, IExchangeError>> LoadAsync(Ticker ticker, CancellationToken cancellationToken);
}
