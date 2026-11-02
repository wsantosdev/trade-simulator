namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class AssetProjectionConsumer(
    IConfiguration configuration,
    AssetProjectionHandler handler,
    ILogger<AssetProjectionConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Queue = "trade-simulator.portfolio.read-model";

    protected override string ExchangeName => RabbitMqResponsePublisher.ExchangeName;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [AssetChanged.RoutingKey];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
