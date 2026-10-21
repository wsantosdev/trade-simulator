using TradeSimulator.OfferBook.Domain.Enums;

namespace TradeSimulator.OfferBook.Common;

// Quantity 0 means the level left the window or was removed.
public sealed record LevelChange(BookSide Side, decimal Price, long Quantity, int Orders);
