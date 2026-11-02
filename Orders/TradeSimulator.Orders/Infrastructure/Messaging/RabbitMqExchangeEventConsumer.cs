using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Orders.Infrastructure.Telemetry;

namespace TradeSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqExchangeEventConsumer(
    IConfiguration configuration,
    ExchangeEventMessageHandler handler,
    ILogger<RabbitMqExchangeEventConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    protected override ServiceTelemetry Telemetry => OrdersTelemetry.Service;
    protected override string ExchangeName => "trade-simulator.exchange.order.events";
    protected override string QueueName => "trade-simulator.orders.order.events";
    protected override IReadOnlyList<string> RoutingKeys => ["#"];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
