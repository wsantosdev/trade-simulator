namespace TradingSimulator.Exchange.Domain.Entities;

public enum SessionPhase : byte
{
    PreOpen,
    Open,
    Closed
}

public readonly record struct SessionPosition(SessionPhase Phase, DateOnly Date, int Step);

// Simulated B3 session: 10:00-18:00 in Brasilia time (UTC-3, no daylight saving), one step per second.
public static class TradingSession
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);
    public static readonly TimeSpan OpeningTime = TimeSpan.FromHours(10);
    public const int Steps = 8 * 60 * 60;

    public static SessionPosition At(DateTimeOffset now)
    {
        var local = now.ToOffset(Offset);
        var date = DateOnly.FromDateTime(local.DateTime);
        var seconds = (int)Math.Floor((local.TimeOfDay - OpeningTime).TotalSeconds);
        return seconds switch
        {
            < 0 => new SessionPosition(SessionPhase.PreOpen, date, 0),
            >= Steps => new SessionPosition(SessionPhase.Closed, date, Steps),
            _ => new SessionPosition(SessionPhase.Open, date, seconds)
        };
    }
}
