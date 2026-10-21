namespace TradeSimulator.Exchange.Infrastructure.Messaging;

public static class OfferBookTopology
{
    // Exchange -> OfferBook: EventEnvelope<OfferBookSnapshot|OfferBookDelta>, routing key "offerbook.{TICKER}".
    public const string EventsExchange = "trade-simulator.exchange.offerbook.events";

    // OfferBook -> Exchange: heartbeat with the tickers that have subscribers, and snapshot requests.
    public const string InterestExchange = "trade-simulator.offerbook.interest.events";
    public const string InterestQueue = "trade-simulator.exchange.offerbook.interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    public static string RoutingKey(string ticker) => $"offerbook.{ticker}";
}
