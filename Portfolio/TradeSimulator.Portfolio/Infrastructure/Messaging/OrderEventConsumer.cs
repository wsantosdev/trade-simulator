namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class OrderEventConsumer(
    IConfiguration configuration,
    OrderEventMessageHandler handler,
    ILogger<OrderEventConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator-order-commands";
    public const string Queue = "trade-simulator.portfolio.order.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [PortfolioEventTypes.OrderSubmitted, PortfolioEventTypes.OrderScheduleCancelled];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
