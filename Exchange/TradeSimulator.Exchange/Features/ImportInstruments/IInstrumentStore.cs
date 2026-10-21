
namespace TradeSimulator.Exchange.Features.ImportInstruments;

public interface IInstrumentStore
{
    Task<UpsertOutcome> UpsertAsync(IReadOnlyCollection<InstrumentRecord> instruments, CancellationToken cancellationToken);
}

public readonly record struct UpsertOutcome(int Inserted, int Updated);
