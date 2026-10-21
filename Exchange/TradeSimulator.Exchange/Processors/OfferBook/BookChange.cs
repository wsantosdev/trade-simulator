using TradeSimulator.Exchange.Domain.Enums;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Processors.OfferBook;

// A level that changed on a side of the book; a zero quantity means the level disappeared.
public sealed record BookChange(OrderSide Side, Price Price, Quantity Quantity, int Orders);
