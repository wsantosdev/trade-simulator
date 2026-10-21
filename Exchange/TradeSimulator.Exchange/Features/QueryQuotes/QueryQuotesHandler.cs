using Moonad;
using TradeSimulator.Exchange.Domain;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Features.QueryQuotes;

// Inbound port.
public interface IQueryQuotes
{
    Task<Result<IReadOnlyList<QuoteItem>, IExchangeError>> ExecuteAsync(
        string ticker, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}

// Outbound port.
public interface IQuoteQueries
{
    Task<IReadOnlyList<QuoteItem>> HistoryAsync(Ticker ticker, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}

public sealed class QueryQuotesHandler(IQuoteQueries queries) : IQueryQuotes
{
    public async Task<Result<IReadOnlyList<QuoteItem>, IExchangeError>> ExecuteAsync(
        string ticker, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            return Result<IReadOnlyList<QuoteItem>, IExchangeError>.Error(parsed.ErrorValue);
        if (from > to)
            return Result<IReadOnlyList<QuoteItem>, IExchangeError>.Error(new InvalidDateRange());

        return Result<IReadOnlyList<QuoteItem>, IExchangeError>.Ok(
            await queries.HistoryAsync(parsed.ResultValue, from, to, cancellationToken));
    }
}
