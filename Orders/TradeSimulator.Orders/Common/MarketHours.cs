namespace TradeSimulator.Orders.Common;

public interface IMarketHours
{
    bool IsOpen();
}

// The exchange runs 24/7, so the market is always open.
public sealed class MarketHours : IMarketHours
{
    public bool IsOpen() => true;
}
