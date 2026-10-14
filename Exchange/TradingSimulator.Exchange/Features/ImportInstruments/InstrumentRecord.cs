using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.ImportInstruments;

public sealed record InstrumentRecord(InstrumentId Id, Ticker Ticker, string Name);
