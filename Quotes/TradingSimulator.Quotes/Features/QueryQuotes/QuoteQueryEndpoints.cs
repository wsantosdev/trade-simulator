using TradingSimulator.Quotes.Common;
using TradingSimulator.Quotes.Domain.ValueObjects;

namespace TradingSimulator.Quotes.Features.QueryQuotes;

public static class QuoteQueryEndpoints
{
    public static IEndpointRouteBuilder MapQuoteQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quotes/{ticker}/latest", (string ticker, LatestQuotes latest) =>
        {
            var parsed = Ticker.TryCreate(ticker);
            if (parsed.IsError)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid ticker");

            var quote = latest.Find(parsed.ResultValue);
            return quote is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Quote not available")
                : Results.Ok(quote);
        });

        return endpoints;
    }
}
