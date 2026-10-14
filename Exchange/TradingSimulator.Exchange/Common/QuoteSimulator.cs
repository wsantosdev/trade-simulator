using Microsoft.Extensions.Logging;
using TradingSimulator.Exchange.Domain;
using TradingSimulator.Exchange.Domain.Entities;
using TradingSimulator.Exchange.Domain.Enums;
using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Common;

// Keeps the intraday state of every ticker with subscribers in memory (nothing is persisted: after a restart the
// day is simulated again from the opening) and emits one quote per active ticker for each cycle.
// Calls are serialized by a gate: the quote cycle and the match engine (user orders) both use it.
public sealed class QuoteSimulator(
    IQuoteSimulationSource source,
    IQuotePublisher publisher,
    QuoteInterestRegistry interest,
    Random random,
    ILogger<QuoteSimulator> logger)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleRetention = TimeSpan.FromMinutes(10);

    private readonly Dictionary<Ticker, TickerState> _states = [];
    private readonly Dictionary<Ticker, DateTimeOffset> _retryAfter = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunCycleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var ticker in interest.ActiveTickers())
        {
            var quote = await NextQuoteAsync(ticker, now, cancellationToken);
            if (quote is not null)
                await publisher.PublishAsync(EventEnvelope.Of(quote), cancellationToken);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Prune(now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Quote?> NextQuoteAsync(Ticker ticker, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await NextQuoteCoreAsync(ticker, now, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Quote?> NextQuoteCoreAsync(Ticker ticker, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var position = TradingSession.At(now);
        var state = await StateFor(ticker, position.Date, now, cancellationToken);
        if (state is null)
            return null;

        state.LastUsed = now;
        // A second is simulated once: the quote cache keeps the match engine and the quote cycle from advancing it twice.
        var second = now.ToUnixTimeSeconds();
        if (state.CachedQuote is not null && state.CachedSecond == second)
            return state.CachedQuote;

        var parameters = state.Parameters;
        Quote quote;
        if (position.Phase == SessionPhase.PreOpen)
        {
            var previousClose = parameters.PreviousClose;
            quote = new Quote(ticker.Value, previousClose, previousClose, previousClose, previousClose, previousClose,
                QuoteVariation.Unchanged, now);
        }
        else
        {
            state.Walk.AdvanceTo(position.Step);
            var walk = state.Walk;
            quote = new Quote(ticker.Value, parameters.Open, parameters.PreviousClose, walk.High, walk.Low, walk.Last,
                VariationOf(walk.Last, state.PreviousLast), now);
        }

        state.PreviousLast = quote.Last;
        state.CachedQuote = quote;
        state.CachedSecond = second;
        return quote;
    }

    private async Task<TickerState?> StateFor(
        Ticker ticker, DateOnly date, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_states.TryGetValue(ticker, out var existing) && existing.Date == date)
            return existing;
        if (_retryAfter.TryGetValue(ticker, out var retryAfter) && now < retryAfter)
            return null;

        var loaded = await source.LoadAsync(ticker, cancellationToken);
        if (loaded.IsError)
        {
            logger.LogWarning("No simulation parameters for {Ticker}: {Error}.", ticker, loaded.ErrorValue.GetType().Name);
            _retryAfter[ticker] = now + RetryDelay;
            return null;
        }

        _retryAfter.Remove(ticker);
        var state = new TickerState(date, loaded.ResultValue, new IntradayWalk(loaded.ResultValue, random));
        _states[ticker] = state;
        return state;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (ticker, state) in _states)
        {
            if (now - state.LastUsed > IdleRetention)
                _states.Remove(ticker);
        }
    }

    private static QuoteVariation VariationOf(decimal last, decimal? previous) =>
        previous is null || last == previous ? QuoteVariation.Unchanged
        : last > previous ? QuoteVariation.Up
        : QuoteVariation.Down;

    private sealed class TickerState(DateOnly date, SimulationParameters parameters, IntradayWalk walk)
    {
        public DateOnly Date { get; } = date;
        public SimulationParameters Parameters { get; } = parameters;
        public IntradayWalk Walk { get; } = walk;
        public decimal? PreviousLast { get; set; }
        public DateTimeOffset LastUsed { get; set; }
        public Quote? CachedQuote { get; set; }
        public long CachedSecond { get; set; }
    }
}
