using TradingSimulator.Exchange.Application;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Api.Features.QuoteQueries;

public sealed record QuoteHistoryRequest(string Ticker, DateOnly? From, DateOnly? To);

public static class QuoteQueryEndpoints
{
    public static IEndpointRouteBuilder MapQuoteQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quotes/{ticker}", async (
            [AsParameters] QuoteHistoryRequest request,
            IQuoteQueries queries,
            CancellationToken cancellationToken) =>
        {
            var ticker = Ticker.TryCreate(request.Ticker);
            if (ticker.IsError)
                return ticker.ErrorValue.ToHttpResult();
            if (request.From > request.To)
                return new InvalidDateRange().ToHttpResult();

            return Results.Ok(await queries.HistoryAsync(ticker.ResultValue, request.From, request.To, cancellationToken));
        });

        return endpoints;
    }
}
