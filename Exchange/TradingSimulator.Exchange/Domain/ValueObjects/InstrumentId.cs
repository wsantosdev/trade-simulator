using Moonad;

namespace TradingSimulator.Exchange.Domain.ValueObjects;

public readonly record struct InstrumentId
{
    public Guid Guid { get; }

    private InstrumentId(Guid guid) => Guid = guid;

    public static InstrumentId New() => new(Guid.CreateVersion7());

    public static Result<InstrumentId, IExchangeError> TryCreate(Guid value) =>
        value != Guid.Empty && value.Version == 7
            ? new InstrumentId(value)
            : new InvalidInstrumentId();

    public override string ToString() => Guid.ToString();
}
