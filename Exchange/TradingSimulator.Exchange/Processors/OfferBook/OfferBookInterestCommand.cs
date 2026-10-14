using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Processors.OfferBook;

// Requests from the consumers of the order book, already converted to Exchange types.
public abstract record OfferBookInterestCommand;

public sealed record RenewOfferBookInterest(IReadOnlyList<Ticker> Tickers) : OfferBookInterestCommand;

public sealed record RequestOfferBookSnapshot(Ticker Ticker) : OfferBookInterestCommand;
