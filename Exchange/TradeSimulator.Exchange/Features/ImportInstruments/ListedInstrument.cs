using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Features.ImportInstruments;

public sealed record ListedInstrument(string Ticker, string Name);
