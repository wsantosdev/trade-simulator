using TradingSimulator.Quotes.Domain;

namespace TradingSimulator.Quotes.Features.GetLatestQuote;

public static class GetLatestQuoteEndpoint
{
    public static IEndpointRouteBuilder MapGetLatestQuoteEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quotes/{ticker}/latest", (string ticker, IGetLatestQuote getLatestQuote) =>
        {
            var result = getLatestQuote.Execute(ticker);
            if (!result.IsError)
                return Results.Ok(result.ResultValue);

            return result.ErrorValue switch
            {
                QuoteNotAvailable => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Quote not available"),
                _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid ticker")
            };
        });

        return endpoints;
    }
}
