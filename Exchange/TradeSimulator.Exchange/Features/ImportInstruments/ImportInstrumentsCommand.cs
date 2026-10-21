using TradeSimulator.Exchange.Infrastructure.Http;

namespace TradeSimulator.Exchange.Features.ImportInstruments;

// Command-line entry point: `dotnet run -- --import-instruments <BVBG.028 xml path>`.
// It is deliberately not an HTTP endpoint, so no caller can make the API read arbitrary server files.
public static class ImportInstrumentsCommand
{
    public const string Argument = "--import-instruments";

    public static async Task<int> RunAsync(
        ImportInstrumentsHandler handler, string path, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(path, cancellationToken);
        if (result.IsError)
        {
            logger.LogError("Instrument import failed: {Reason}", result.ErrorValue.Describe());
            return 1;
        }

        var summary = result.ResultValue;
        logger.LogInformation(
            "Instrument import finished: {Read} read, {Skipped} skipped, {Inserted} inserted, {Updated} updated, {Unchanged} unchanged.",
            summary.Read, summary.Skipped, summary.Inserted, summary.Updated, summary.Unchanged);
        return 0;
    }
}
