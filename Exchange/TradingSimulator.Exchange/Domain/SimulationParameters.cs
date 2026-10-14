using Moonad;

namespace TradingSimulator.Exchange.Domain;

// Inputs of the intraday simulation, taken from the historical quotes:
// the most recent trading day (Open, TargetClose and its range), the close before it (PreviousClose)
// and the lowest/highest prices of the whole series (Floor/Ceiling), which a simulated price never crosses.
public sealed record SimulationParameters
{
    private const double RangeToSigma = 1.6;
    private const double MinimumRelativeSigma = 0.002;

    public Ticker Ticker { get; }
    public decimal Open { get; }
    public decimal PreviousClose { get; }
    public decimal TargetClose { get; }
    public decimal Floor { get; }
    public decimal Ceiling { get; }

    // Standard deviation of the whole session, so that the expected range of the walk matches the day's real range.
    public double DailyVolatility { get; }

    private SimulationParameters(
        Ticker ticker, decimal open, decimal previousClose, decimal targetClose,
        decimal floor, decimal ceiling, double dailyVolatility)
    {
        Ticker = ticker;
        Open = open;
        PreviousClose = previousClose;
        TargetClose = targetClose;
        Floor = floor;
        Ceiling = ceiling;
        DailyVolatility = dailyVolatility;
    }

    public static Result<SimulationParameters, IExchangeError> TryCreate(
        Ticker ticker, decimal open, decimal previousClose, decimal targetClose,
        decimal dayHigh, decimal dayLow, decimal floor, decimal ceiling)
    {
        decimal[] prices = [open, previousClose, targetClose, dayHigh, dayLow, floor, ceiling];
        if (prices.Any(price => price <= 0) || floor > dayLow || ceiling < dayHigh || dayLow > dayHigh)
            return new InvalidSimulationParameters();

        var volatility = Math.Max((double)(dayHigh - dayLow) / RangeToSigma, (double)open * MinimumRelativeSigma);
        return new SimulationParameters(ticker, open, previousClose, targetClose, floor, ceiling, volatility);
    }
}
