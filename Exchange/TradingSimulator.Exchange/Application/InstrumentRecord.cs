using TradingSimulator.Exchange.Domain;

namespace TradingSimulator.Exchange.Application;

public sealed record InstrumentRecord(InstrumentId Id, Ticker Ticker, string Name);
