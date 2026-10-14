using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Infrastructure.Http;

public static class ExchangeErrorHttpResults
{
    public static IResult ToHttpResult(this IExchangeError error)
    {
        var (status, title, detail) = error switch
        {
            InvalidTicker => (StatusCodes.Status400BadRequest, "Invalid ticker", "A ticker must have four alphanumeric characters starting with a letter, followed by one or two digits (for example PETR4)."),
            InvalidDateRange => (StatusCodes.Status400BadRequest, "Invalid date range", "The start date must not be after the end date."),
            InstrumentNotFound => (StatusCodes.Status404NotFound, "Instrument not found", "No instrument is registered with this ticker."),
            _ => (StatusCodes.Status500InternalServerError, "Exchange error", "The request could not be processed.")
        };

        return Results.Problem(statusCode: status, title: title, detail: detail);
    }

    public static string Describe(this IExchangeError error) => error switch
    {
        InstrumentFileNotFound => "The instrument file was not found.",
        MalformedInstrumentFile => "The instrument file is not a valid BVBG.028 XML.",
        NoInstrumentsInFile => "The instrument file has no cash-market equities.",
        QuoteFileNotFound => "The quote file was not found.",
        MalformedQuoteFile => "The quote file is not a valid COTAHIST file.",
        NoQuotesInFile => "The quote file has no cash-market quotes.",
        _ => error.GetType().Name
    };
}
