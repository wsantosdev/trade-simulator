using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Account.Infrastructure.Telemetry;

public static class AccountTelemetry
{
    public const string Name = "TradeSimulator.Account";

    public static readonly ServiceTelemetry Service = new(Name, "account");
}
