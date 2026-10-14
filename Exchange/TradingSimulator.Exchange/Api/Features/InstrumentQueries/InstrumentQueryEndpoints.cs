using TradingSimulator.Exchange.Application;
using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Api.Features.InstrumentQueries;

public sealed record SearchInstrumentsRequest(string? Search, int? Page);

public static class InstrumentQueryEndpoints
{
    public static IEndpointRouteBuilder MapInstrumentQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/instruments", async (
            [AsParameters] SearchInstrumentsRequest request,
            IInstrumentQueries queries,
            CancellationToken cancellationToken) =>
            Results.Ok(await queries.SearchAsync(request.Search, request.Page ?? 1, cancellationToken)));

        endpoints.MapGet("/api/instruments/{ticker}", async (
            string ticker,
            IInstrumentQueries queries,
            CancellationToken cancellationToken) =>
        {
            var parsed = Ticker.TryCreate(ticker);
            if (parsed.IsError)
                return parsed.ErrorValue.ToHttpResult();

            var instrument = await queries.GetAsync(parsed.ResultValue, cancellationToken);
            return instrument.IsError ? instrument.ErrorValue.ToHttpResult() : Results.Ok(instrument.ResultValue);
        });

        return endpoints;
    }
}
