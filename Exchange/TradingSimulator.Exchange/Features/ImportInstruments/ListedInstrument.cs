using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.ImportInstruments;

public sealed record ListedInstrument(string Ticker, string Name);
