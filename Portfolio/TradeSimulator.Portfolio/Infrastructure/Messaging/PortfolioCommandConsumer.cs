namespace TradeSimulator.Portfolio.Infrastructure.Messaging;

public sealed class PortfolioCommandConsumer(
    IConfiguration configuration,
    PortfolioCommandMessageHandler handler,
    ILogger<PortfolioCommandConsumer> logger) : RabbitMqConsumerBase(configuration, logger)
{
    public const string Exchange = "trade-simulator.portfolio.command";
    public const string Queue = "trade-simulator.portfolio.asset.commands";

    protected override string ExchangeName => Exchange;
    protected override string QueueName => Queue;
    protected override IReadOnlyList<string> RoutingKeys => ["#"];

    protected override Task HandleAsync(string body, CancellationToken cancellationToken) =>
        handler.HandleAsync(body, cancellationToken);
}
