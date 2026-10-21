using Npgsql;
using TradingSimulator.Exchange.Infrastructure.Http;
using TradingSimulator.Exchange.Features.ImportInstruments;
using TradingSimulator.Exchange.Features.ImportQuotes;

namespace TradingSimulator.Exchange.Infrastructure.Seeding;

// Imports the B3 files found in `Seed:Directory` on startup, only into tables that are still empty.
// It reads a configured directory (never a request-supplied path), so it does not reopen the arbitrary-file-read
// risk that keeps the imports out of HTTP. Runs in the background so the API is available while a large file loads.
public sealed class DataSeedService(
    NpgsqlDataSource dataSource,
    ImportInstrumentsHandler instruments,
    ImportQuotesHandler quotes,
    IConfiguration configuration,
    ILogger<DataSeedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = configuration["Seed:Directory"];
        if (string.IsNullOrWhiteSpace(directory))
            return;

        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Seed directory {Directory} does not exist; skipping the initial data load.", directory);
            return;
        }

        try
        {
            await SeedInstrumentsAsync(directory, stoppingToken);
            await SeedQuotesAsync(directory, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Initial data load failed; the Exchange keeps running without it.");
        }
    }

    private async Task SeedInstrumentsAsync(string directory, CancellationToken cancellationToken)
    {
        if (await HasRowsAsync("instruments", cancellationToken))
            return;

        var file = Newest(directory, "BVBG*.xml");
        if (file is null)
        {
            logger.LogWarning("No BVBG*.xml file in {Directory}; instruments were not loaded.", directory);
            return;
        }

        logger.LogInformation("Loading instruments from {File}.", file);
        var result = await instruments.HandleAsync(file, cancellationToken);
        if (result.IsError)
            logger.LogError("Instrument load failed: {Reason}", result.ErrorValue.Describe());
        else
            logger.LogInformation("Instrument load finished: {Read} read, {Inserted} inserted.",
                result.ResultValue.Read, result.ResultValue.Inserted);
    }

    private async Task SeedQuotesAsync(string directory, CancellationToken cancellationToken)
    {
        if (await HasRowsAsync("quotes", cancellationToken))
            return;

        var file = Newest(directory, "COTAHIST*.TXT") ?? Newest(directory, "COTACOES_*.TXT");
        if (file is null)
        {
            logger.LogWarning("No COTAHIST*.TXT or COTACOES_*.TXT file in {Directory}; quotes were not loaded.", directory);
            return;
        }

        logger.LogInformation("Loading quotes from {File}.", file);
        var result = await quotes.HandleAsync(file, cancellationToken);
        if (result.IsError)
            logger.LogError("Quote load failed: {Reason}", result.ErrorValue.Describe());
        else
            logger.LogInformation("Quote load finished: {Read} read, {Inserted} inserted.",
                result.ResultValue.Read, result.ResultValue.Inserted);
    }

    private async Task<bool> HasRowsAsync(string table, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT EXISTS (SELECT 1 FROM {table})");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static string? Newest(string directory, string pattern) =>
        new DirectoryInfo(directory)
            .EnumerateFiles(pattern, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
}
