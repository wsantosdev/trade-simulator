namespace TradeSimulator.Portfolio.Domain;

public interface IPortfolioError;

public readonly struct InvalidAssetId : IPortfolioError;
public readonly struct InvalidAccountId : IPortfolioError;
public readonly struct InvalidTicker : IPortfolioError;
public readonly struct InvalidSortField : IPortfolioError;
public readonly struct InvalidQuantity : IPortfolioError;
public readonly struct QuantityAboveLimit : IPortfolioError;
public readonly struct QuantityOverflow : IPortfolioError;
public readonly struct AssetNotFound : IPortfolioError;
public readonly struct InsufficientAvailableQuantity : IPortfolioError;
public readonly struct InsufficientBlockedQuantity : IPortfolioError;
public readonly struct AssetOpeningEventMissing : IPortfolioError;
public readonly struct CorruptAssetHistory : IPortfolioError;
public readonly struct ConcurrencyConflict : IPortfolioError;
public readonly struct AssetAlreadyExists : IPortfolioError;
public readonly struct DuplicateEnvelope : IPortfolioError;
public readonly struct InvalidEnvelopeId : IPortfolioError;
public readonly struct MalformedCommand : IPortfolioError;
public readonly struct UnsupportedCommand : IPortfolioError;
