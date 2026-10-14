using Moonad;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Features.Shared;

namespace TradingSimulator.Exchange.Features.GetInstrument;

// Inbound port.
public interface IGetInstrument
{
    Task<Result<InstrumentItem, IExchangeError>> ExecuteAsync(string ticker, CancellationToken cancellationToken);
}

// Outbound port.
public interface IInstrumentLookup
{
    Task<Result<InstrumentItem, IExchangeError>> GetAsync(Ticker ticker, CancellationToken cancellationToken);
}

public sealed class GetInstrumentHandler(IInstrumentLookup lookup) : IGetInstrument
{
    public async Task<Result<InstrumentItem, IExchangeError>> ExecuteAsync(string ticker, CancellationToken cancellationToken)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            return Result<InstrumentItem, IExchangeError>.Error(parsed.ErrorValue);

        return await lookup.GetAsync(parsed.ResultValue, cancellationToken);
    }
}
