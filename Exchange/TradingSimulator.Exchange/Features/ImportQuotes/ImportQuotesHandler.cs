using Microsoft.Extensions.Logging;
using Moonad;
using TradingSimulator.Exchange.Features.Shared;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.ImportQuotes;

public sealed class ImportQuotesHandler(
    IQuoteSource source,
    IQuoteStore store,
    ILogger<ImportQuotesHandler> logger)
{
    public const int BatchSize = 5000;

    public async Task<Result<ImportSummary, IExchangeError>> HandleAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new QuoteFileNotFound();

        int read = 0, skipped = 0, inserted = 0, updated = 0;
        var batch = new Dictionary<(string, DateOnly), DailyQuote>(BatchSize);

        try
        {
            await foreach (var listed in source.ReadAsync(path, cancellationToken))
            {
                read++;
                var quote = DailyQuote.TryCreate(
                    listed.Ticker, listed.TradeDate, listed.Open, listed.High, listed.Low, listed.Close);
                if (quote.IsError)
                {
                    skipped++;
                    continue;
                }

                // A repeated (ticker, date) in the same batch would make the upsert touch one row twice; the last one wins.
                batch[(quote.ResultValue.Ticker.Value, quote.ResultValue.TradeDate)] = quote.ResultValue;
                if (batch.Count < BatchSize)
                    continue;

                var outcome = await store.UpsertAsync(batch.Values, cancellationToken);
                (inserted, updated) = (inserted + outcome.Inserted, updated + outcome.Updated);
                batch.Clear();
            }

            if (batch.Count > 0)
            {
                var outcome = await store.UpsertAsync(batch.Values, cancellationToken);
                (inserted, updated) = (inserted + outcome.Inserted, updated + outcome.Updated);
            }
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Quote file {Path} could not be parsed.", path);
            return new MalformedQuoteFile();
        }

        if (read == 0)
            return new NoQuotesInFile();

        return new ImportSummary(read, skipped, inserted, updated, Math.Max(read - skipped - inserted - updated, 0));
    }
}
