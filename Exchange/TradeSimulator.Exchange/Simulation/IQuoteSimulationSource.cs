using Moonad;
using TradeSimulator.Exchange.Domain;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Simulation;

public interface IQuoteSimulationSource
{
    Task<Result<SimulationParameters, IExchangeError>> LoadAsync(Ticker ticker, CancellationToken cancellationToken);
}
