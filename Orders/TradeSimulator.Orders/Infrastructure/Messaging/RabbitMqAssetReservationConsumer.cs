namespace TradeSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqAssetReservationConsumer(
    IConfiguration configuration,
    AssetReservationMessageHandler handler,
    ILogger<RabbitMqAssetReservationConsumer> logger) : RabbitMqReservationConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator.portfolio.reservations";
    public const string Queue = "trade-simulator.orders.portfolio.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override string Description => "asset reservation";

    protected override Task ProcessAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
