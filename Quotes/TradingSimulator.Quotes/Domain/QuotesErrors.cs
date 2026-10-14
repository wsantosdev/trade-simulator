namespace TradingSimulator.Quotes.Domain;

public interface IQuotesError;

public readonly struct InvalidTicker : IQuotesError;
public readonly struct QuoteNotAvailable : IQuotesError;
public readonly struct MalformedMessage : IQuotesError;
