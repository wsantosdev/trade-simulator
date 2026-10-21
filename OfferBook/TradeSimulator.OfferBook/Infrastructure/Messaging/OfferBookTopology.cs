using TradeSimulator.OfferBook.Processors.OfferBookInterest;
using TradeSimulator.OfferBook.Common;

namespace TradeSimulator.OfferBook.Infrastructure.Messaging;

public static class OfferBookTopology
{
    // Exchange -> OfferBook: EventEnvelope<OfferBookSnapshot|OfferBookDelta>, routing key "offerbook.{TICKER}".
    public const string BookEventsExchange = "trade-simulator.exchange.offerbook.events";
    public const string BookQueue = "trade-simulator.offerbook.book.events";

    // OfferBook -> Exchange: EventEnvelope<OfferBookInterestRenewed|OfferBookSnapshotRequested>, routing key "interest".
    public const string InterestExchange = "trade-simulator.offerbook.interest.events";
    public const string InterestQueue = "trade-simulator.exchange.offerbook.interest";
    public const string InterestRoutingKey = "interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    // Book messages are perishable: a backlog is useless, so old or excess messages are discarded by the broker.
    public const int BookMessageTtlMilliseconds = 10_000;
    public const int BookMaxLength = 10_000;
}
