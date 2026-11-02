using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Orders.Infrastructure.ReadModel;
using TradeSimulator.Orders.Infrastructure.Telemetry;

namespace TradeSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqOrderProjectionConsumer(
    IConfiguration configuration,
    OrderProjectionHandler handler,
    MongoOrderReadModel readModel,
    ILogger<RabbitMqOrderProjectionConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    public const string Queue = "trade-simulator.orders.read-model";

    protected override ServiceTelemetry Telemetry => OrdersTelemetry.Service;
    protected override string ExchangeName => RabbitMqCommandPublisher.EventsExchangeName;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => ["#"];

    protected override Task OnConnectingAsync(CancellationToken cancellationToken) =>
        readModel.EnsureIndexesAsync(cancellationToken);

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
