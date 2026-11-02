using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Exchange.Infrastructure.Telemetry;

public static class ExchangeTelemetry
{
    public const string Name = "TradeSimulator.Exchange";

    public static readonly ServiceTelemetry Service = new(Name, "exchange");
}
