using TradingSimulator.Portfolio.Common;
using TradingSimulator.Portfolio.Infrastructure.Serialization;

namespace TradingSimulator.Portfolio.Infrastructure.Messaging;

public sealed class OrderEventConsumer(
    IConfiguration configuration,
    PortfolioCommandProcessor processor,
    ILogger<OrderEventConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trading-simulator-order-commands";
    public const string Queue = "trading-simulator.portfolio.order.events";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => [PortfolioEventTypes.OrderSubmitted];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        processor.ProcessOrderEventAsync(body, PortfolioJson.Options, cancellationToken);
}
