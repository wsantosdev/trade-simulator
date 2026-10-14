using TradingSimulator.Exchange.Application;

namespace TradingSimulator.Exchange.Api.Features.QuoteImport;

// Command-line entry point: `dotnet run -- --import-quotes <COTAHIST txt path>`.
// Like the instrument import, it is not an HTTP endpoint, so the API never reads arbitrary server files.
public static class ImportQuotesCommand
{
    public const string Argument = "--import-quotes";

    public static async Task<int> RunAsync(
        ImportQuotesHandler handler, string path, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(path, cancellationToken);
        if (result.IsError)
        {
            logger.LogError("Quote import failed: {Reason}", result.ErrorValue.Describe());
            return 1;
        }

        var summary = result.ResultValue;
        logger.LogInformation(
            "Quote import finished: {Read} read, {Skipped} skipped, {Inserted} inserted, {Updated} updated, {Unchanged} unchanged.",
            summary.Read, summary.Skipped, summary.Inserted, summary.Updated, summary.Unchanged);
        return 0;
    }
}
