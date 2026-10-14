using TradingSimulator.Exchange.Domain.Entities;
using TradingSimulator.Exchange.Domain.Enums;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Infrastructure.Messaging;

namespace TradingSimulator.Exchange.Common;

// The published view of a book: the best levels of each side. The delta is the difference between two windows.
public sealed record OfferBookWindow(IReadOnlyList<OfferBookLevel> Bids, IReadOnlyList<OfferBookLevel> Asks)
{
    public const int Depth = 10;

    public static OfferBookWindow Of(OrderBook book) =>
        new(Project(book.Levels(OrderSide.Buy, Depth)), Project(book.Levels(OrderSide.Sell, Depth)));

    public static IReadOnlyList<OfferBookChange> Diff(OfferBookWindow? previous, OfferBookWindow current) =>
    [
        .. DiffSide(OfferBookSide.Bid, previous?.Bids ?? [], current.Bids),
        .. DiffSide(OfferBookSide.Ask, previous?.Asks ?? [], current.Asks)
    ];

    private static IEnumerable<OfferBookChange> DiffSide(
        OfferBookSide side, IReadOnlyList<OfferBookLevel> previous, IReadOnlyList<OfferBookLevel> current)
    {
        var before = previous.ToDictionary(level => level.Price);
        var after = current.ToDictionary(level => level.Price);
        foreach (var level in current)
        {
            if (!before.TryGetValue(level.Price, out var old) || old != level)
                yield return new OfferBookChange(side, level.Price, level.Quantity, level.Orders);
        }

        foreach (var level in previous)
        {
            if (!after.ContainsKey(level.Price))
                yield return new OfferBookChange(side, level.Price, 0, 0);
        }
    }

    private static IReadOnlyList<OfferBookLevel> Project(IReadOnlyList<BookLevel> levels) =>
        [.. levels.Select(level => new OfferBookLevel(level.Price.Value, level.Quantity.Value, level.Orders))];
}
