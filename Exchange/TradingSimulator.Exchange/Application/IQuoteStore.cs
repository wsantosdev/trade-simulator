using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public interface IQuoteStore
{
    Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<DailyQuote> quotes, CancellationToken cancellationToken);
}
