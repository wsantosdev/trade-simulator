using TradeSimulator.Exchange.Domain.Entities;

namespace TradeSimulator.Exchange.Simulation;

// Brownian bridge from the opening price to the target closing price, one step per second.
// The latent price is continuous and Last is rounded to the cent; it is always kept within [Floor, Ceiling],
// so High and Low never leave that band either.
public sealed class IntradayWalk
{
    private readonly SimulationParameters _parameters;
    private readonly Random _random;
    private readonly double _stepVolatility;
    private double _latent;

    public IntradayWalk(SimulationParameters parameters, Random random)
    {
        _parameters = parameters;
        _random = random;
        _stepVolatility = parameters.DailyVolatility / Math.Sqrt(TradingSession.Steps);
        _latent = (double)parameters.Open;
        Last = High = Low = parameters.Open;
    }

    public int Step { get; private set; }
    public decimal Last { get; private set; }
    public decimal High { get; private set; }
    public decimal Low { get; private set; }

    public void AdvanceTo(int step)
    {
        var target = Math.Min(step, TradingSession.Steps);
        while (Step < target)
            Advance();
    }

    private void Advance()
    {
        var remaining = TradingSession.Steps - Step;
        var target = (double)_parameters.TargetClose;
        var mean = _latent + (target - _latent) / remaining;
        var deviation = _stepVolatility * Math.Sqrt((remaining - 1d) / remaining);
        _latent = Math.Clamp(mean + deviation * NextGaussian(), (double)_parameters.Floor, (double)_parameters.Ceiling);
        Step++;

        Last = Step == TradingSession.Steps
            ? _parameters.TargetClose
            : Math.Clamp(Math.Round((decimal)_latent, 2), _parameters.Floor, _parameters.Ceiling);
        High = Math.Max(High, Last);
        Low = Math.Min(Low, Last);
    }

    private double NextGaussian()
    {
        var u1 = 1d - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2);
    }
}
