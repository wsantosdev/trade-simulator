namespace TradingSimulator.Exchange.Features.SearchInstruments;

public sealed record SearchInstrumentsRequest(string? Search, int? Page);

public static class SearchInstrumentsEndpoint
{
    public static IEndpointRouteBuilder MapSearchInstrumentsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/instruments", async (
            [AsParameters] SearchInstrumentsRequest request,
            ISearchInstruments searchInstruments,
            CancellationToken cancellationToken) =>
            Results.Ok(await searchInstruments.ExecuteAsync(request.Search, request.Page, cancellationToken)));

        return endpoints;
    }
}
