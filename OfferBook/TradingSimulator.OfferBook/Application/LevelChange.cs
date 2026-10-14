namespace TradingSimulator.OfferBook.Application;

// Quantity 0 means the level left the window or was removed.
public sealed record LevelChange(BookSide Side, decimal Price, long Quantity, int Orders);
