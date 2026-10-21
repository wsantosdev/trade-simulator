namespace TradeSimulator.Orders.Common;

public interface IMarketHours
{
    bool IsOpen();
}

// Simulated B3 session: 10:00-18:00 in Brasilia time (UTC-3). The offset shifts the clock to exercise the session off-hours;
// it must match the Exchange's Simulation:SessionClockOffset.
public sealed class MarketHours(TimeProvider clock, TimeSpan clockOffset) : IMarketHours
{
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);
    private static readonly TimeSpan Opening = TimeSpan.FromHours(10);
    private static readonly TimeSpan Closing = TimeSpan.FromHours(18);

    public bool IsOpen()
    {
        var time = (clock.GetUtcNow() + clockOffset).ToOffset(BrasiliaOffset).TimeOfDay;
        return time >= Opening && time < Closing;
    }
}
