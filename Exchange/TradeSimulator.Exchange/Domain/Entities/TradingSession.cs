namespace TradeSimulator.Exchange.Domain.Entities;

public enum SessionPhase : byte
{
    PreOpen,
    Open,
    Closed
}

public readonly record struct SessionPosition(SessionPhase Phase, DateOnly Date, int Step);

// The exchange runs 24/7: the session is always open. A simulated day is the Brasilia calendar day
// (UTC-3, no daylight saving), from 00:00 to 24:00, one step per second.
public static class TradingSession
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);
    public const int Steps = 24 * 60 * 60;

    public static SessionPosition At(DateTimeOffset now)
    {
        var local = now.ToOffset(Offset);
        var date = DateOnly.FromDateTime(local.DateTime);
        var seconds = Math.Min((int)Math.Floor(local.TimeOfDay.TotalSeconds), Steps - 1);
        return new SessionPosition(SessionPhase.Open, date, seconds);
    }
}
