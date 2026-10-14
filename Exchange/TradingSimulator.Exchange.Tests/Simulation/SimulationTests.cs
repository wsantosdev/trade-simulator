using Microsoft.Extensions.Logging.Abstractions;
using Moonad;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.Entities;
using TradingSimulator.Exchange.Domain.Enums;
using TradingSimulator.Exchange.Domain.ValueObjects;
using TradingSimulator.Exchange.Infrastructure.Messaging;
using TradingSimulator.Exchange.Infrastructure.Serialization;
using TradingSimulator.Exchange.Processors.Quotes;
using TradingSimulator.Exchange.Simulation;

namespace TradingSimulator.Exchange.Tests.Simulation;

public sealed class SimulationTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(-3));
    private static readonly Ticker Petr = Ticker.TryCreate("PETR4").ResultValue;

    private static SimulationParameters Parameters(decimal floor = 30m, decimal ceiling = 40m) =>
        SimulationParameters.TryCreate(Petr, 35m, 34.5m, 36m, 36.5m, 34m, floor, ceiling).ResultValue;

    [Fact]
    public void Session_phases()
    {
        Assert.Equal(SessionPhase.PreOpen, TradingSession.At(Day.AddHours(9.99)).Phase);
        var open = TradingSession.At(Day.AddHours(10).AddSeconds(5));
        Assert.Equal((SessionPhase.Open, 5), (open.Phase, open.Step));
        Assert.Equal(SessionPhase.Closed, TradingSession.At(Day.AddHours(18)).Phase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Walk_respects_bounds_and_ends_at_target(int seed)
    {
        var parameters = Parameters(floor: 33.9m, ceiling: 36.6m);
        var walk = new IntradayWalk(parameters, new Random(seed));
        for (var step = 1; step <= TradingSession.Steps; step++)
        {
            walk.AdvanceTo(step);
            Assert.InRange(walk.Last, parameters.Floor, parameters.Ceiling);
            Assert.True(walk.Low <= walk.Last && walk.Last <= walk.High);
            Assert.Equal(walk.Last, decimal.Round(walk.Last, 2));
        }

        Assert.Equal(parameters.TargetClose, walk.Last);
    }

    [Fact]
    public void Invalid_parameters_are_rejected()
    {
        var result = SimulationParameters.TryCreate(Petr, 35m, 34m, 36m, 36m, 34m, 35m, 40m);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Simulator_emits_phases_and_variation()
    {
        var simulator = new QuoteSimulator(
            new FakeSource(Parameters()), new FakePublisher(), new QuoteInterestRegistry(TimeProvider.System),
            new Random(7), NullLogger<QuoteSimulator>.Instance);

        var pre = await simulator.NextQuoteAsync(Petr, Day.AddHours(9), default);
        Assert.NotNull(pre);
        Assert.Equal((34.5m, QuoteVariation.Unchanged), (pre.Last, pre.Variation));
        Assert.Equal(pre.Close, pre.Open);

        var first = await simulator.NextQuoteAsync(Petr, Day.AddHours(10).AddSeconds(1), default);
        Assert.Equal(35m, first!.Open);
        Assert.Equal(34.5m, first.Close);

        var later = await simulator.NextQuoteAsync(Petr, Day.AddHours(12), default);
        var expected = later!.Last == first.Last ? QuoteVariation.Unchanged
            : later.Last > first.Last ? QuoteVariation.Up : QuoteVariation.Down;
        Assert.Equal(expected, later.Variation);

        var closed = await simulator.NextQuoteAsync(Petr, Day.AddHours(19), default);
        Assert.Equal(36m, closed!.Last);
    }

    [Fact]
    public async Task Simulator_publishes_only_for_active_tickers_and_skips_unknown()
    {
        var publisher = new FakePublisher();
        var registry = new QuoteInterestRegistry(TimeProvider.System);
        var simulator = new QuoteSimulator(
            new FakeSource(Parameters()), publisher, registry, new Random(1), NullLogger<QuoteSimulator>.Instance);

        await simulator.RunCycleAsync(Day.AddHours(11), default);
        Assert.Empty(publisher.Published);

        registry.Renew([Petr]);
        await simulator.RunCycleAsync(Day.AddHours(11), default);
        Assert.Equal("PETR4", Assert.Single(publisher.Published).Ticker);
    }

    [Fact]
    public void Interest_registry_expires_with_time()
    {
        var time = new Clock();
        var registry = new QuoteInterestRegistry(time);
        registry.Renew([Petr]);
        Assert.Single(registry.ActiveTickers());
        time.Now += QuoteInterestRegistry.TimeToLive + TimeSpan.FromSeconds(1);
        Assert.Empty(registry.ActiveTickers());
    }

    [Fact]
    public void Interest_parser_accepts_valid_and_rejects_malformed()
    {
        var body = System.Text.Json.JsonSerializer.Serialize(
            EventEnvelope.Of(new QuoteInterestRenewed(["PETR4", "??"])), MessageJson.Options);

        Assert.Single(QuoteInterestParser.Parse(body, MessageJson.Options).ResultValue);
        Assert.True(QuoteInterestParser.Parse("{not json", MessageJson.Options).IsError);
        Assert.True(QuoteInterestParser.Parse("{}", MessageJson.Options).IsError);
    }

    private sealed class FakeSource(SimulationParameters parameters) : IQuoteSimulationSource
    {
        public Task<Result<SimulationParameters, IExchangeError>> LoadAsync(Ticker ticker, CancellationToken ct) =>
            Task.FromResult<Result<SimulationParameters, IExchangeError>>(
                ticker == parameters.Ticker ? parameters : new NoQuoteHistory());
    }

    private sealed class FakePublisher : IQuotePublisher
    {
        public List<Quote> Published { get; } = [];

        public Task PublishAsync(Quote quote, CancellationToken ct)
        {
            Published.Add(quote);
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
