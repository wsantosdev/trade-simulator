namespace TradeSimulator.OfferBook.Common;

// Total quantity and number of offers resting at one price.
public sealed record PriceLevel(decimal Price, long Quantity, int Orders);
