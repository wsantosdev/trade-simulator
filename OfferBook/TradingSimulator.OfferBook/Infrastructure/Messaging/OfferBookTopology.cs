using TradingSimulator.OfferBook.Processors.OfferBookInterest;
using TradingSimulator.OfferBook.Common;

namespace TradingSimulator.OfferBook.Infrastructure.Messaging;

public static class OfferBookTopology
{
    // Exchange -> OfferBook: EventEnvelope<OfferBookSnapshot|OfferBookDelta>, routing key "offerbook.{TICKER}".
    public const string BookEventsExchange = "trading-simulator.exchange.offerbook.events";
    public const string BookQueue = "trading-simulator.offerbook.book.events";

    // OfferBook -> Exchange: EventEnvelope<OfferBookInterestRenewed|OfferBookSnapshotRequested>, routing key "interest".
    public const string InterestExchange = "trading-simulator.offerbook.interest.events";
    public const string InterestQueue = "trading-simulator.exchange.offerbook.interest";
    public const string InterestRoutingKey = "interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    // Book messages are perishable: a backlog is useless, so old or excess messages are discarded by the broker.
    public const int BookMessageTtlMilliseconds = 10_000;
    public const int BookMaxLength = 10_000;
}
