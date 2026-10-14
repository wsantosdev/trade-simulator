using TradingSimulator.Orders.Application;

namespace TradingSimulator.Orders.Infrastructure;

public sealed class RabbitMqFundsReservationConsumer(
    IConfiguration configuration,
    FundsReservationProcessor processor,
    ILogger<RabbitMqFundsReservationConsumer> logger) : RabbitMqReservationConsumerBase(configuration, logger)
{
    public const string Exchange = "trading-simulator.account.reservations";
    public const string Queue = "trading-simulator.orders.account.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override string Description => "funds reservation";

    protected override Task ProcessAsync(string body, CancellationToken cancellationToken) =>
        processor.ProcessAsync(body, OrderJson.Options, cancellationToken);
}
