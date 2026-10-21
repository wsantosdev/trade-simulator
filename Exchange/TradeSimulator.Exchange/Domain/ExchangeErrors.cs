namespace TradeSimulator.Exchange.Domain;

public interface IExchangeError;

public readonly struct InvalidInstrumentId : IExchangeError;
public readonly struct InvalidTicker : IExchangeError;
public readonly struct InstrumentNotFound : IExchangeError;
public readonly struct InstrumentFileNotFound : IExchangeError;
public readonly struct MalformedInstrumentFile : IExchangeError;
public readonly struct NoInstrumentsInFile : IExchangeError;
public readonly struct InvalidPrice : IExchangeError;
public readonly struct InvalidQuote : IExchangeError;
public readonly struct InvalidDateRange : IExchangeError;
public readonly struct QuoteFileNotFound : IExchangeError;
public readonly struct MalformedQuoteFile : IExchangeError;
public readonly struct NoQuotesInFile : IExchangeError;
public readonly struct NoQuoteHistory : IExchangeError;
public readonly struct InvalidSimulationParameters : IExchangeError;
public readonly struct MalformedMessage : IExchangeError;
public readonly struct InvalidOrderId : IExchangeError;
public readonly struct InvalidQuantity : IExchangeError;
public readonly struct InvalidOrder : IExchangeError;
public readonly struct PriceNotOnTick : IExchangeError;
public readonly struct OrderAlreadyExists : IExchangeError;
public readonly struct OrderNotResting : IExchangeError;
