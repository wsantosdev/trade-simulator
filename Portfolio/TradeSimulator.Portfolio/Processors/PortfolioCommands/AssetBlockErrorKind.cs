namespace TradeSimulator.Portfolio.Processors.PortfolioCommands;

public enum AssetBlockErrorKind : byte
{
    InsufficientQuantity,
    AssetNotFound,
    InvalidOrder
}
