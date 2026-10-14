namespace TradingSimulator.Exchange.Infrastructure;

public static class OfferBookTopology
{
    // Exchange -> OfferBook: EventEnvelope<OfferBookSnapshot|OfferBookDelta>, routing key "offerbook.{TICKER}".
    public const string EventsExchange = "trading-simulator.exchange.offerbook.events";

    // OfferBook -> Exchange: heartbeat with the tickers that have subscribers, and snapshot requests.
    public const string InterestExchange = "trading-simulator.offerbook.interest.events";
    public const string InterestQueue = "trading-simulator.exchange.offerbook.interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    public static string RoutingKey(string ticker) => $"offerbook.{ticker}";
}
