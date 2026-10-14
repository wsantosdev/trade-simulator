using TradingSimulator.OfferBook.Domain;

namespace TradingSimulator.OfferBook.Features.GetOfferBook;

public static class GetOfferBookEndpoint
{
    public static IEndpointRouteBuilder MapGetOfferBookEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/offerbook/{ticker}", (string ticker, IGetOfferBook getOfferBook) =>
        {
            var result = getOfferBook.Execute(ticker);
            if (!result.IsError)
                return Results.Ok(result.ResultValue);

            return result.ErrorValue switch
            {
                OfferBookNotAvailable => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Offer book not available"),
                _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid ticker")
            };
        });

        return endpoints;
    }
}
