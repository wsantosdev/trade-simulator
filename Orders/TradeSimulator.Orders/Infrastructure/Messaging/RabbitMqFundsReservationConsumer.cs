namespace TradeSimulator.Orders.Infrastructure.Messaging;

public sealed class RabbitMqFundsReservationConsumer(
    IConfiguration configuration,
    FundsReservationMessageHandler handler,
    ILogger<RabbitMqFundsReservationConsumer> logger) : RabbitMqReservationConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator.account.reservations";
    public const string Queue = "trade-simulator.orders.account.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override string Description => "funds reservation";

    protected override Task ProcessAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
