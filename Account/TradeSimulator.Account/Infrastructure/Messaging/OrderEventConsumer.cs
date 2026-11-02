using TradeSimulator.Account.Infrastructure.Telemetry;
using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;

namespace TradeSimulator.Account.Infrastructure.Messaging;

public sealed class OrderEventConsumer(
    IConfiguration configuration,
    OrderEventHandler handler,
    ILogger<OrderEventConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    public const string Queue = "trade-simulator.account.order.events";

    protected override ServiceTelemetry Telemetry => AccountTelemetry.Service;
    protected override string ExchangeName => OrderExecutionEvent.ExchangeName;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => OrderExecutionEvent.RoutingKeys;

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
