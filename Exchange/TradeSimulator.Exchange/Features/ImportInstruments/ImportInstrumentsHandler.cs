using Microsoft.Extensions.Logging;
using Moonad;
using TradeSimulator.Exchange.Features.Shared;
using TradeSimulator.Exchange.Domain;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Features.ImportInstruments;

public sealed class ImportInstrumentsHandler(
    IInstrumentSource source,
    IInstrumentStore store,
    ILogger<ImportInstrumentsHandler> logger)
{
    public const int BatchSize = 1000;

    public async Task<Result<ImportSummary, IExchangeError>> HandleAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new InstrumentFileNotFound();

        int read = 0, skipped = 0, inserted = 0, updated = 0;
        var batch = new Dictionary<string, InstrumentRecord>(BatchSize);

        try
        {
            await foreach (var listed in source.ReadAsync(path, cancellationToken))
            {
                read++;
                var ticker = Ticker.TryCreate(listed.Ticker);
                if (ticker.IsError || string.IsNullOrWhiteSpace(listed.Name))
                {
                    skipped++;
                    continue;
                }

                // A repeated ticker in the same batch would make the upsert touch one row twice; the last one wins.
                batch[ticker.ResultValue.Value] = new InstrumentRecord(InstrumentId.New(), ticker.ResultValue, listed.Name);
                if (batch.Count < BatchSize)
                    continue;

                (inserted, updated) = Add(inserted, updated, await store.UpsertAsync(batch.Values, cancellationToken));
                batch.Clear();
            }

            if (batch.Count > 0)
                (inserted, updated) = Add(inserted, updated, await store.UpsertAsync(batch.Values, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Instrument file {Path} could not be parsed.", path);
            return new MalformedInstrumentFile();
        }

        if (read == 0)
            return new NoInstrumentsInFile();

        var unchanged = read - skipped - inserted - updated;
        return new ImportSummary(read, skipped, inserted, updated, Math.Max(unchanged, 0));
    }

    private static (int, int) Add(int inserted, int updated, UpsertOutcome outcome) =>
        (inserted + outcome.Inserted, updated + outcome.Updated);
}
