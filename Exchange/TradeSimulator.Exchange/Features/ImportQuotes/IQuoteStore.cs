using TradeSimulator.Exchange.Domain.ValueObjects;
using TradeSimulator.Exchange.Features.ImportInstruments;

namespace TradeSimulator.Exchange.Features.ImportQuotes;

public interface IQuoteStore
{
    Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<DailyQuote> quotes, CancellationToken cancellationToken);
}
