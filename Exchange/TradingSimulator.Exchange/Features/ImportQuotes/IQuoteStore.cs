using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Features.ImportInstruments;

namespace TradingSimulator.Exchange.Features.ImportQuotes;

public interface IQuoteStore
{
    Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<DailyQuote> quotes, CancellationToken cancellationToken);
}
