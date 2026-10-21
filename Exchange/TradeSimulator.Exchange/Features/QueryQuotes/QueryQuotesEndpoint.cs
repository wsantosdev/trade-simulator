using TradeSimulator.Exchange.Infrastructure.Http;

namespace TradeSimulator.Exchange.Features.QueryQuotes;

public sealed record QuoteHistoryRequest(string Ticker, DateOnly? From, DateOnly? To);

public static class QueryQuotesEndpoint
{
    public static IEndpointRouteBuilder MapQueryQuotesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/quotes/{ticker}", async (
            [AsParameters] QuoteHistoryRequest request,
            IQueryQuotes queryQuotes,
            CancellationToken cancellationToken) =>
        {
            var result = await queryQuotes.ExecuteAsync(request.Ticker, request.From, request.To, cancellationToken);
            return result.IsError ? result.ErrorValue.ToHttpResult() : Results.Ok(result.ResultValue);
        });

        return endpoints;
    }
}
