using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Portfolio.Infrastructure.Telemetry;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class OrderEventConsumer(
    IConfiguration configuration,
    OrderEventHandler handler,
    ILogger<OrderEventConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    public const string Queue = "trade-simulator.portfolio.order.events";

    protected override ServiceTelemetry Telemetry => PortfolioTelemetry.Service;
    protected override string ExchangeName => OrderExecutionEvent.ExchangeName;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => OrderExecutionEvent.RoutingKeys;

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
