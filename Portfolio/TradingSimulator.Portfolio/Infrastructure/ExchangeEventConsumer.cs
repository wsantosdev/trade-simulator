using TradingSimulator.Portfolio.Application;

namespace TradingSimulator.Portfolio.Infrastructure;

public sealed class ExchangeEventConsumer(
    IConfiguration configuration,
    PortfolioCommandProcessor processor,
    ILogger<ExchangeEventConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trading-simulator.exchange.order.events";
    public const string Queue = "trading-simulator.portfolio.exchange.events";

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
        processor.ProcessExchangeEventAsync(body, PortfolioJson.Options, cancellationToken);
}
