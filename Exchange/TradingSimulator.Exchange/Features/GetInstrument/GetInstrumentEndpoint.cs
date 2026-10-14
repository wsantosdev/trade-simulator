using TradingSimulator.Exchange.Infrastructure.Http;

namespace TradingSimulator.Exchange.Features.GetInstrument;

public static class GetInstrumentEndpoint
{
    public static IEndpointRouteBuilder MapGetInstrumentEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/instruments/{ticker}", async (
            string ticker,
            IGetInstrument getInstrument,
            CancellationToken cancellationToken) =>
        {
            var result = await getInstrument.ExecuteAsync(ticker, cancellationToken);
            return result.IsError ? result.ErrorValue.ToHttpResult() : Results.Ok(result.ResultValue);
        });

        return endpoints;
    }
}
