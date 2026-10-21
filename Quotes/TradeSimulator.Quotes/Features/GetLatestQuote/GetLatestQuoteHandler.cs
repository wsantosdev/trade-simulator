using Moonad;
using TradeSimulator.Quotes.Common;
using TradeSimulator.Quotes.Domain;
using TradeSimulator.Quotes.Domain.ValueObjects;

namespace TradeSimulator.Quotes.Features.GetLatestQuote;

// Inbound port.
public interface IGetLatestQuote
{
    Result<Quote, IQuotesError> Execute(string ticker);
}

public sealed class GetLatestQuoteHandler(LatestQuotes latest) : IGetLatestQuote
{
    public Result<Quote, IQuotesError> Execute(string ticker)
    {
        var parsed = Ticker.TryCreate(ticker);
        if (parsed.IsError)
            return Result<Quote, IQuotesError>.Error(parsed.ErrorValue);

        var quote = latest.Find(parsed.ResultValue);
        return quote is null
            ? Result<Quote, IQuotesError>.Error(new QuoteNotAvailable())
            : Result<Quote, IQuotesError>.Ok(quote);
    }
}
