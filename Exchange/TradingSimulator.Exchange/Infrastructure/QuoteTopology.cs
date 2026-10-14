namespace TradingSimulator.Exchange.Infrastructure;

public static class QuoteTopology
{
    // Exchange -> Quotes: EventEnvelope<Quote>, routing key "quote.{TICKER}".
    public const string QuoteEventsExchange = "trading-simulator.exchange.quote.events";

    // Quotes -> Exchange: EventEnvelope<QuoteInterestRenewed>, a heartbeat listing the tickers with subscribers.
    public const string InterestExchange = "trading-simulator.quotes.interest.events";
    public const string InterestQueue = "trading-simulator.exchange.quote.interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    public static string RoutingKey(string ticker) => $"quote.{ticker}";
}
