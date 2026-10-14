using TradingSimulator.OfferBook.Processors.OfferBookUpdates;
using TradingSimulator.OfferBook.Processors.OfferBookInterest;
using System.Text.Json;
using TradingSimulator.OfferBook.Common;
using TradingSimulator.OfferBook.Domain.Enums;
using TradingSimulator.OfferBook.Domain.ValueObjects;
using TradingSimulator.OfferBook.Infrastructure.Messaging;
using TradingSimulator.OfferBook.Infrastructure.Serialization;

namespace TradingSimulator.OfferBook.Tests;

public sealed class OfferBookTests
{
    private static readonly Ticker Petr = Ticker.TryCreate("petr4").ResultValue;
    private static readonly Ticker Vale = Ticker.TryCreate("VALE3").ResultValue;

    private static OfferBookSnapshot Snapshot(long sequence = 10, string ticker = "PETR4") => new(
        ticker, sequence, BookPhase.Open,
        [new PriceLevel(10.00m, 100, 1), new PriceLevel(10.02m, 300, 3), new PriceLevel(10.01m, 200, 2)],
        [new PriceLevel(10.05m, 500, 2), new PriceLevel(10.03m, 400, 1)],
        DateTimeOffset.UnixEpoch);

    private static OfferBookDelta Delta(long sequence, long previous, params LevelChange[] changes) =>
        new("PETR4", sequence, previous, BookPhase.Open, changes, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Ticker_is_validated_and_normalized()
    {
        Assert.Equal("PETR4", Petr.Value);
        Assert.True(Ticker.TryCreate("X").IsError);
    }

    [Fact]
    public void Snapshot_is_stored_with_bids_descending_and_asks_ascending()
    {
        var latest = new LatestOfferBooks();
        latest.ApplySnapshot(Snapshot());

        var book = latest.Find(Petr)!;
        Assert.Equal([10.02m, 10.01m, 10.00m], book.Bids.Select(level => level.Price));
        Assert.Equal([10.03m, 10.05m], book.Asks.Select(level => level.Price));
        Assert.Equal(10, book.Sequence);
        Assert.Null(latest.Find(Vale));
    }

    [Fact]
    public void Contiguous_delta_updates_inserts_and_removes_levels()
    {
        var latest = new LatestOfferBooks();
        latest.ApplySnapshot(Snapshot());

        var outcome = latest.ApplyDelta(Delta(11, 10,
            new LevelChange(BookSide.Bid, 10.01m, 250, 4),
            new LevelChange(BookSide.Bid, 10.02m, 0, 0),
            new LevelChange(BookSide.Ask, 10.04m, 50, 1)));

        Assert.Equal(DeltaOutcome.Applied, outcome);
        var book = latest.Find(Petr)!;
        Assert.Equal(11, book.Sequence);
        Assert.Equal([new PriceLevel(10.01m, 250, 4), new PriceLevel(10.00m, 100, 1)], book.Bids);
        Assert.Equal([10.03m, 10.04m, 10.05m], book.Asks.Select(level => level.Price));
    }

    [Fact]
    public void Empty_delta_advances_the_sequence()
    {
        var latest = new LatestOfferBooks();
        latest.ApplySnapshot(Snapshot());

        Assert.Equal(DeltaOutcome.Applied, latest.ApplyDelta(Delta(11, 10)));
        Assert.Equal(DeltaOutcome.Applied, latest.ApplyDelta(Delta(12, 11)));
        Assert.Equal(12, latest.Find(Petr)!.Sequence);
    }

    [Fact]
    public void Gap_discards_the_base_and_asks_for_a_snapshot()
    {
        var latest = new LatestOfferBooks();
        latest.ApplySnapshot(Snapshot());

        Assert.Equal(DeltaOutcome.SnapshotNeeded, latest.ApplyDelta(Delta(13, 12)));
        Assert.Null(latest.Find(Petr));
    }

    [Fact]
    public void Stale_delta_is_ignored_and_keeps_the_book()
    {
        var latest = new LatestOfferBooks();
        latest.ApplySnapshot(Snapshot());

        Assert.Equal(DeltaOutcome.Ignored, latest.ApplyDelta(Delta(10, 9)));
        Assert.Equal(10, latest.Find(Petr)!.Sequence);
    }

    [Fact]
    public void Snapshot_restarts_the_base_with_any_sequence()
    {
        var latest = new LatestOfferBooks();
        latest.ApplySnapshot(Snapshot(50));
        latest.ApplySnapshot(Snapshot(3));

        Assert.Equal(3, latest.Find(Petr)!.Sequence);
    }

    [Fact]
    public void Delta_before_snapshot_requests_it_at_most_once_per_interval()
    {
        var clock = new ManualTime();
        var latest = new LatestOfferBooks(clock);

        Assert.Equal(DeltaOutcome.SnapshotNeeded, latest.ApplyDelta(Delta(5, 4)));
        Assert.Equal(DeltaOutcome.Ignored, latest.ApplyDelta(Delta(6, 5)));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(DeltaOutcome.Ignored, latest.ApplyDelta(Delta(7, 6)));
        clock.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(DeltaOutcome.SnapshotNeeded, latest.ApplyDelta(Delta(8, 7)));
    }

    [Fact]
    public async Task Processor_stores_and_forwards_snapshots_and_contiguous_deltas()
    {
        var latest = new LatestOfferBooks();
        var notifier = new FakeNotifier();
        var requests = new FakeRequests();
        var processor = new OfferBookProcessor(latest, notifier, requests);

        var snapshot = await processor.ProcessAsync(Body(Snapshot()), OfferBookJson.Options, default);
        Assert.Equal(ProcessOutcome.SnapshotStored, snapshot.ResultValue);
        Assert.Single(notifier.Snapshots);
        Assert.Equal(10, latest.Find(Petr)!.Sequence);

        var delta = await processor.ProcessAsync(
            Body(Delta(11, 10, new LevelChange(BookSide.Ask, 10.03m, 0, 0))), OfferBookJson.Options, default);
        Assert.Equal(ProcessOutcome.DeltaForwarded, delta.ResultValue);
        Assert.Equal(11, Assert.Single(notifier.Deltas).Sequence);
        Assert.Equal([10.05m], latest.Find(Petr)!.Asks.Select(level => level.Price));
        Assert.Empty(requests.Sent);
    }

    [Fact]
    public async Task Processor_requests_a_snapshot_on_gap_and_does_not_forward_the_delta()
    {
        var latest = new LatestOfferBooks();
        var notifier = new FakeNotifier();
        var requests = new FakeRequests();
        var processor = new OfferBookProcessor(latest, notifier, requests);
        latest.ApplySnapshot(Snapshot());

        var result = await processor.ProcessAsync(Body(Delta(15, 14)), OfferBookJson.Options, default);

        Assert.Equal(ProcessOutcome.SnapshotRequested, result.ResultValue);
        Assert.Empty(notifier.Deltas);
        Assert.Equal("PETR4", Assert.Single(requests.Sent).Event.Ticker);
    }

    [Fact]
    public async Task Processor_rejects_malformed_messages()
    {
        var notifier = new FakeNotifier();
        var processor = new OfferBookProcessor(new LatestOfferBooks(), notifier, new FakeRequests());

        Assert.True((await processor.ProcessAsync("{bad", OfferBookJson.Options, default)).IsError);
        Assert.True((await processor.ProcessAsync("{}", OfferBookJson.Options, default)).IsError);
        Assert.True((await processor.ProcessAsync(Body(Snapshot(ticker: "??")), OfferBookJson.Options, default)).IsError);
        var unknown = JsonSerializer.Serialize(new EventEnvelope<object>(Guid.CreateVersion7(), "Other", new { }), OfferBookJson.Options);
        Assert.True((await processor.ProcessAsync(unknown, OfferBookJson.Options, default)).IsError);
        Assert.Empty(notifier.Snapshots);
    }

    [Fact]
    public void Contract_json_uses_camel_case_and_string_enums()
    {
        var json = JsonSerializer.Serialize(Delta(11, 10, new LevelChange(BookSide.Bid, 1m, 2, 3)), OfferBookJson.Options);
        Assert.Contains("\"previousSequence\":10", json);
        Assert.Contains("\"phase\":\"Open\"", json);
        Assert.Contains("\"side\":\"Bid\"", json);
    }

    [Fact]
    public void Subscriptions_report_first_follower_and_release_on_disconnect()
    {
        var subscriptions = new OfferBookSubscriptions();
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
    public async Task Heartbeat_publishes_only_when_there_are_subscribers()
    {
        var subscriptions = new OfferBookSubscriptions();
        var interest = new FakeInterest();
        var requests = new FakeRequests();
        var heartbeat = new OfferBookInterestHeartbeat(subscriptions, interest, requests);

        await heartbeat.PublishAsync(default);
        Assert.Empty(interest.Sent);

        subscriptions.Subscribe("a", Petr);
        await heartbeat.PublishAsync(default);
        Assert.Equal(["PETR4"], Assert.Single(interest.Sent).Event.Tickers);
        Assert.Empty(requests.Sent);
    }

    [Fact]
    public async Task First_subscriber_renews_interest_and_requests_a_snapshot()
    {
        var subscriptions = new OfferBookSubscriptions();
        var interest = new FakeInterest();
        var requests = new FakeRequests();
        var heartbeat = new OfferBookInterestHeartbeat(subscriptions, interest, requests);

        subscriptions.Subscribe("a", Petr);
        await heartbeat.FirstSubscriberAsync("PETR4", default);

        Assert.Equal(["PETR4"], Assert.Single(interest.Sent).Event.Tickers);
        Assert.Equal("PETR4", Assert.Single(requests.Sent).Event.Ticker);
    }

    private static string Body<T>(T message) => JsonSerializer.Serialize(EventEnvelope.Of(message), OfferBookJson.Options);

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public void Advance(TimeSpan span) => _now += span;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeNotifier : IOfferBookNotifier
    {
        public List<OfferBookSnapshot> Snapshots { get; } = [];
        public List<OfferBookDelta> Deltas { get; } = [];

        public Task SnapshotAsync(OfferBookSnapshot snapshot, CancellationToken cancellationToken)
        {
            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task DeltaAsync(OfferBookDelta delta, CancellationToken cancellationToken)
        {
            Deltas.Add(delta);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInterest : IOfferBookInterestPublisher
    {
        public List<EventEnvelope<OfferBookInterestRenewed>> Sent { get; } = [];

        public Task PublishAsync(EventEnvelope<OfferBookInterestRenewed> envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRequests : IOfferBookSnapshotRequestPublisher
    {
        public List<EventEnvelope<OfferBookSnapshotRequested>> Sent { get; } = [];

        public Task PublishAsync(EventEnvelope<OfferBookSnapshotRequested> envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
