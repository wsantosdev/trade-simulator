using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Orders.Infrastructure.Telemetry;

public static class OrdersTelemetry
{
    public const string Name = "TradeSimulator.Orders";

    public static readonly ServiceTelemetry Service = new(Name, "orders");
}
