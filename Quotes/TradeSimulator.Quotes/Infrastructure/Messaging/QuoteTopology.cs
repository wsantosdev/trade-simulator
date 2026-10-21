using TradeSimulator.Quotes.Processors.QuoteInterest;
using TradeSimulator.Quotes.Common;

namespace TradeSimulator.Quotes.Infrastructure.Messaging;

public static class QuoteTopology
{
    // Exchange -> Quotes: EventEnvelope<Quote>, routing key "quote.{TICKER}".
    public const string QuoteEventsExchange = "trade-simulator.exchange.quote.events";
    public const string QuoteQueue = "trade-simulator.quotes.quote.events";

    // Quotes -> Exchange: EventEnvelope<QuoteInterestRenewed>.
    public const string InterestExchange = "trade-simulator.quotes.interest.events";
    public const string InterestQueue = "trade-simulator.exchange.quote.interest";
    public const int InterestMessageTtlMilliseconds = 15_000;

    // Quotes are perishable: a backlog is useless, so old or excess messages are discarded by the broker.
    public const int QuoteMessageTtlMilliseconds = 10_000;
    public const int QuoteMaxLength = 10_000;
}
