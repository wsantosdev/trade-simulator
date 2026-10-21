namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class ExchangeEventConsumer(
    IConfiguration configuration,
    ExchangeEventMessageHandler handler,
    ILogger<ExchangeEventConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator.exchange.order.events";
    public const string Queue = "trade-simulator.portfolio.exchange.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;

    protected override IReadOnlyList<string> RoutingKeys =>
    [
        PortfolioEventTypes.OrderPartiallyFilled,
        PortfolioEventTypes.OrderFilled,
        PortfolioEventTypes.OrderCancelled,
        PortfolioEventTypes.OrderRejected
    ];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
