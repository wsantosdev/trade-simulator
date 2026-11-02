namespace TradeSimulator.Portfolio.Features.Shared;

public enum ReservationFailure : byte
{
    InsufficientQuantity,
    AssetNotFound,
    Invalid,
    Conflict
}
