using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Common;

public sealed record InstrumentRecord(InstrumentId Id, Ticker Ticker, string Name);
