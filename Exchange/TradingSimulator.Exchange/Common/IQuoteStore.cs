using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Common;

public interface IQuoteStore
{
    Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<DailyQuote> quotes, CancellationToken cancellationToken);
}
