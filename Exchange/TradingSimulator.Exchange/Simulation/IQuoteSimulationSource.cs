using Moonad;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Simulation;

public interface IQuoteSimulationSource
{
    Task<Result<SimulationParameters, IExchangeError>> LoadAsync(Ticker ticker, CancellationToken cancellationToken);
}
