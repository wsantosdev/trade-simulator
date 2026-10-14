namespace TradingSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqAssetReservationConsumer(
    IConfiguration configuration,
    AssetReservationMessageHandler handler,
    ILogger<RabbitMqAssetReservationConsumer> logger) : RabbitMqReservationConsumerBase(configuration, logger)
{
    public const string Exchange = "trading-simulator.portfolio.reservations";
    public const string Queue = "trading-simulator.orders.portfolio.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override string Description => "asset reservation";

    protected override Task ProcessAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
