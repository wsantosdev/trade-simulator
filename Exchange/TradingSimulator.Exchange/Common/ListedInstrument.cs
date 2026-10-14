using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Common;

public sealed record ListedInstrument(string Ticker, string Name);
