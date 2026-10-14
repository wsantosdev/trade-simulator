namespace TradingSimulator.OfferBook.Domain;

public interface IOfferBookError;

public readonly struct InvalidTicker : IOfferBookError;
public readonly struct OfferBookNotAvailable : IOfferBookError;
public readonly struct MalformedMessage : IOfferBookError;
