using TradeSimulator.Exchange.Domain.Entities;
using TradeSimulator.Exchange.Domain.Enums;
using TradeSimulator.Exchange.Domain.ValueObjects;

namespace TradeSimulator.Exchange.Processors.OfferBook;

// The published view of a book: the best levels of each side. The delta is the difference between two windows.
public sealed record OfferBookWindow(IReadOnlyList<BookLevel> Bids, IReadOnlyList<BookLevel> Asks)
{
    public const int Depth = 10;

    public static OfferBookWindow Of(OrderBook book) =>
        new(book.Levels(OrderSide.Buy, Depth), book.Levels(OrderSide.Sell, Depth));

    public static IReadOnlyList<BookChange> Diff(OfferBookWindow? previous, OfferBookWindow current) =>
    [
        .. DiffSide(OrderSide.Buy, previous?.Bids ?? [], current.Bids),
        .. DiffSide(OrderSide.Sell, previous?.Asks ?? [], current.Asks)
    ];

    private static IEnumerable<BookChange> DiffSide(
        OrderSide side, IReadOnlyList<BookLevel> previous, IReadOnlyList<BookLevel> current)
    {
        var before = previous.ToDictionary(level => level.Price);
        var after = current.ToDictionary(level => level.Price);
        foreach (var level in current)
        {
            if (!before.TryGetValue(level.Price, out var old) || old != level)
                yield return new BookChange(side, level.Price, level.Quantity, level.Orders);
        }

        foreach (var level in previous)
        {
            if (!after.ContainsKey(level.Price))
                yield return new BookChange(side, level.Price, Quantity.Zero, 0);
        }
    }
}
