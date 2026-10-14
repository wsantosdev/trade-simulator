using TradingSimulator.Quotes.Application;
using TradingSimulator.Quotes.Domain;

namespace TradingSimulator.Quotes.Tests;

public sealed class QuotesTests
{
    private static readonly Ticker Petr = Ticker.TryCreate("petr4").ResultValue;
    private static readonly Ticker Vale = Ticker.TryCreate("VALE3").ResultValue;

    [Fact]
    public void Ticker_is_validated_and_normalized()
    {
        Assert.Equal("PETR4", Petr.Value);
        Assert.True(Ticker.TryCreate("X").IsError);
    }

    [Fact]
    public void Subscriptions_report_first_follower_and_release_on_disconnect()
    {
        var subscriptions = new QuoteSubscriptions();
        Assert.True(subscriptions.Subscribe("a", Petr));
        Assert.False(subscriptions.Subscribe("b", Petr));
        Assert.False(subscriptions.Subscribe("b", Petr));
        Assert.True(subscriptions.Subscribe("b", Vale));

        subscriptions.Disconnect("a");
        Assert.Equal(2, subscriptions.ActiveTickers().Count);
        subscriptions.Unsubscribe("b", Petr);
        Assert.Equal(Vale, Assert.Single(subscriptions.ActiveTickers()));
        subscriptions.Disconnect("b");
        Assert.Empty(subscriptions.ActiveTickers());
    }

    [Fact]
    public async Task Processor_caches_and_notifies_valid_quotes_and_rejects_malformed()
    {
        var latest = new LatestQuotes();
        var notifier = new FakeNotifier();
        var processor = new QuoteProcessor(latest, notifier);
        var quote = new Quote("PETR4", 35m, 34.5m, 36m, 34m, 35.5m, QuoteVariation.Up, DateTimeOffset.UnixEpoch);
        var body = System.Text.Json.JsonSerializer.Serialize(EventEnvelope.Of(quote), QuoteJson.Options);

        Assert.False((await processor.ProcessAsync(body, QuoteJson.Options, default)).IsError);
        Assert.Equal(35.5m, latest.Find(Petr)!.Last);
        Assert.Single(notifier.Sent);
        Assert.Contains("\"Up\"", body);

        Assert.True((await processor.ProcessAsync("{bad", QuoteJson.Options, default)).IsError);
        Assert.True((await processor.ProcessAsync("{}", QuoteJson.Options, default)).IsError);
        Assert.Single(notifier.Sent);
    }

    [Fact]
    public async Task Heartbeat_publishes_only_when_there_are_subscribers()
    {
        var subscriptions = new QuoteSubscriptions();
        var publisher = new FakePublisher();
        var heartbeat = new QuoteInterestHeartbeat(subscriptions, publisher);

        await heartbeat.PublishAsync(default);
        Assert.Empty(publisher.Sent);

        subscriptions.Subscribe("a", Petr);
        await heartbeat.PublishAsync(default);
        Assert.Equal(["PETR4"], Assert.Single(publisher.Sent).Event.Tickers);
    }

    private sealed class FakeNotifier : IQuoteNotifier
    {
        public List<Quote> Sent { get; } = [];

        public Task QuoteUpdatedAsync(Quote quote, CancellationToken cancellationToken)
        {
            Sent.Add(quote);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublisher : IQuoteInterestPublisher
    {
        public List<EventEnvelope<QuoteInterestRenewed>> Sent { get; } = [];

        public Task PublishAsync(EventEnvelope<QuoteInterestRenewed> envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
