using TradingSimulator.OfferBook.Common;
using TradingSimulator.OfferBook.Domain.ValueObjects;

namespace TradingSimulator.OfferBook.Features.QueryOfferBook;

public static class OfferBookQueryEndpoints
{
    public static IEndpointRouteBuilder MapOfferBookQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/offerbook/{ticker}", (string ticker, LatestOfferBooks latest) =>
        {
            var parsed = Ticker.TryCreate(ticker);
            if (parsed.IsError)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid ticker");

            var book = latest.Find(parsed.ResultValue);
            return book is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Offer book not available")
                : Results.Ok(book);
        });

        return endpoints;
    }
}
