namespace TradingSimulator.Quotes.Infrastructure;

public static class QuoteTopology
{
    // Exchange -> Quotes: EventEnvelope<Quote>, routing key "quote.{TICKER}".
    public const string QuoteEventsExchange = "trading-simulator.exchange.quote.events";
    public const string QuoteQueue = "trading-simulator.quotes.quote.events";

    // Quotes -> Exchange: EventEnvelope<QuoteInterestRenewed>.
    public const string InterestExchange = "trading-simulator.quotes.interest.events";
    public const string InterestQueue = "trading-simulator.exchange.quote.interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    // Quotes are perishable: a backlog is useless, so old or excess messages are discarded by the broker.
    public const int QuoteMessageTtlMilliseconds = 10_000;
    public const int QuoteMaxLength = 10_000;
}
