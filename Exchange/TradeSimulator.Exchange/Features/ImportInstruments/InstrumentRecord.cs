using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Features.ImportInstruments;

public sealed record InstrumentRecord(InstrumentId Id, Ticker Ticker, string Name);
