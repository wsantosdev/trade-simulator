using TradeSimulator.Commons.Messaging;
using TradeSimulator.Commons.Telemetry;
using TradeSimulator.Portfolio.Infrastructure.Telemetry;

namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class AssetProjectionConsumer(
    IConfiguration configuration,
    AssetProjectionHandler handler,
    ILogger<AssetProjectionConsumer> logger) : RabbitMqConsumer(configuration, logger)
{
    public const string Queue = "trade-simulator.portfolio.read-model";

    protected override ServiceTelemetry Telemetry => PortfolioTelemetry.Service;
    protected override string ExchangeName => RabbitMqEventPublisher.ExchangeName;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [AssetChanged.RoutingKey];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
